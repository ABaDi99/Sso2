using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using SsoServer.Data;
using SsoServer.Entities.Identity;
using SsoServer.Security;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SsoServer.Endpoints;

public static class AuthorizationEndpoints
{
    public static void MapAuthorizationEndpoints(this WebApplication app)
    {
        // ===== /connect/authorize =====
        app.MapMethods("/connect/authorize", new[] { "GET", "POST" }, async (
            HttpContext context,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext db) =>
        {
            var request = context.Features.Get<OpenIddictServerAspNetCoreFeature>()?.Transaction?.Request
                ?? throw new InvalidOperationException("Requête OpenIddict introuvable.");

            // L'utilisateur a-t-il une session active ?
            var result = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);

            // Forcer la reconnexion si : pas de session, OU le client demande prompt=login.
            if (!result.Succeeded || request.HasPrompt(Prompts.Login))
            {
                // Ferme la session existante pour forcer une vraie ré-authentification
                // (sinon Challenge ne fait rien si déjà connecté).
                if (result.Succeeded)
                    await context.SignOutAsync(IdentityConstants.ApplicationScheme);

                // Retire "prompt" de l'URL de retour, sinon boucle infinie après le login.
                var parameters = context.Request.Query
                    .Where(p => p.Key != Parameters.Prompt)
                    .ToDictionary(p => p.Key, p => (string?)p.Value.ToString());
                var returnUrl = QueryHelpers.AddQueryString(
                    context.Request.PathBase + context.Request.Path, parameters);

                return Results.Challenge(
                    new AuthenticationProperties { RedirectUri = returnUrl },
                    new[] { IdentityConstants.ApplicationScheme });
            }

            // Connecté → on construit l'identité à mettre dans les tokens
            var user = await userManager.GetUserAsync(result.Principal!)
                ?? throw new InvalidOperationException("Utilisateur introuvable.");

            // Le compte a pu être désactivé/suspendu après l'ouverture de la
            // session Identity : un cookie déjà valide ne doit pas suffire à
            // obtenir de nouveaux jetons. On ferme la session et on renvoie
            // vers le login, qui affichera le message explicite.
            var status = await AccountStatusChecker.CheckAsync(db, user);
            if (status.IsBlocked)
            {
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);

                var parameters = context.Request.Query
                    .ToDictionary(p => p.Key, p => (string?)p.Value.ToString());
                var returnUrl = QueryHelpers.AddQueryString(
                    context.Request.PathBase + context.Request.Path, parameters);

                return Results.Challenge(
                    new AuthenticationProperties { RedirectUri = returnUrl },
                    new[] { IdentityConstants.ApplicationScheme });
            }

            var identity = new ClaimsIdentity(
                authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role);

            identity.SetClaim(Claims.Subject, await userManager.GetUserIdAsync(user))
                    .SetClaim(Claims.Email, await userManager.GetEmailAsync(user))
                    .SetClaim(Claims.Name, await userManager.GetUserNameAsync(user));

            // Rôles globaux + rôles applicatifs assignés spécifiquement pour
            // le client qui demande ce jeton (union, jamais un remplacement :
            // un rôle global comme Admin doit continuer à fonctionner partout).
            var globalRoles = await userManager.GetRolesAsync(user);
            var appRoles = await db.UserApplicationRoles
                .Where(x => x.UserId == user.Id && x.ClientId == request.ClientId)
                .Select(x => x.Role.Name!)
                .ToListAsync();

            foreach (var role in globalRoles.Concat(appRoles).Distinct())
                identity.AddClaim(Claims.Role, role);

            identity.SetScopes(request.GetScopes());

            // Quels claims vont dans quel token
            identity.SetDestinations(claim => claim.Type switch
            {
                Claims.Email => new[] { Destinations.AccessToken, Destinations.IdentityToken },
                Claims.Name => new[] { Destinations.AccessToken, Destinations.IdentityToken },
                Claims.Role => new[] { Destinations.AccessToken, Destinations.IdentityToken },
                _ => new[] { Destinations.AccessToken }
            });

            return Results.SignIn(
                new ClaimsPrincipal(identity),
                properties: null,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        });

        // ===== /connect/token =====
        app.MapPost("/connect/token", async (
            HttpContext context,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext db) =>
        {
            var request = context.Features.Get<OpenIddictServerAspNetCoreFeature>()?.Transaction?.Request
                ?? throw new InvalidOperationException("Requête OpenIddict introuvable.");

            // OpenIddict rejette déjà les grant types non activés via AllowXxxFlow()
            // avant d'arriver ici ; ce garde-fou ne couvre que le cas où ce
            // comportement changerait, et doit renvoyer une erreur OAuth2
            // standard plutôt que de laisser fuiter une exception .NET.
            if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
                return Results.Forbid(
                    authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnsupportedGrantType,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Le grant type demandé n'est pas pris en charge."
                    }));

            // OpenIddict a déjà validé le code : on récupère l'identité associée
            var result = await context.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            if (result.Principal is null)
                return Results.Forbid(
                    authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme });

            var user = await userManager.FindByIdAsync(result.Principal.GetClaim(Claims.Subject)!);

            if (user is null)
                return Results.Forbid(
                    authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme });

            // C'est ici que se ferment réellement les sessions déjà
            // ouvertes : un compte désactivé/suspendu après coup ne doit
            // plus pouvoir renouveler son access_token, qu'il s'agisse d'un
            // authorization_code fraîchement échangé ou d'un refresh_token
            // utilisé des heures plus tard. OpenIddict traite un tel rejet
            // comme n'importe quel refresh_token invalide : côté ClientApi,
            // TokenRefreshService le reçoit déjà comme un échec (n'importe
            // quel statut non-2xx) et bascule en ReauthRequired.
            var status = await AccountStatusChecker.CheckAsync(db, user);
            if (status.IsBlocked)
                return Results.Forbid(
                    authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Ce compte n'est plus autorisé à se connecter."
                    }));

            return Results.SignIn(
                result.Principal,
                properties: null,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        });


        // ===== /connect/userinfo =====
        app.MapGet("/connect/userinfo", async (
            HttpContext context,
            UserManager<ApplicationUser> userManager) =>
        {
            var result = await context.AuthenticateAsync(
                OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

            if (result.Principal is null)
                return Results.Unauthorized();

            var user = await userManager.FindByIdAsync(result.Principal.GetClaim(Claims.Subject)!);

            if (user is null)
                return Results.Unauthorized();

            return Results.Ok(new Dictionary<string, object>
            {
                [Claims.Subject] = user.Id,
                [Claims.Email] = user.Email!,
                [Claims.Name] = user.UserName!,
                [Claims.Role] = await userManager.GetRolesAsync(user)
            });
        });
        // Pas de .RequireAuthorization() ici : il utiliserait le schéma
        // d'authentification par défaut (cookie Identity) et redirigerait
        // vers /Account/Login (302) au lieu de traiter le Bearer token.
        // Le contrôle d'accès est déjà fait juste au-dessus, explicitement
        // via le schéma OpenIddictValidation (le bon schéma pour un access
        // token), avec un vrai 401 si absent/invalide.


        // Déconnexion initiée par une application cliente (RP-initiated logout).
        // Le passthrough nous donne la main après qu'OpenIddict a validé la
        // requête ; c'est à nous de fermer la session Identity, qu'OpenIddict
        // ne connaît pas.
        app.MapMethods("/connect/logout", ["GET", "POST"], async (
            SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();

            // Rend la main à OpenIddict : il valide le post_logout_redirect_uri
            // contre ceux enregistrés pour le client, puis y redirige.
            // RedirectUri sert de repli si le client n'en a pas fourni.
            return Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        });






    }
}
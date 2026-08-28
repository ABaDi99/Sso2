using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClientApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ClientApi.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth");

        group.MapGet("/health", () => Results.Ok(new { status = "test badi" })).AllowAnonymous();//autorise 

        group.MapGet("/secret", (ClaimsPrincipal user) =>
        {
            return Results.Ok(new
            {
                message = "Donnée réservée aux utilisateurs authentifiés",
                pour = user.FindFirstValue(ClaimTypes.Email),
                genere = DateTime.UtcNow.ToString("HH:mm:ss")
            });
        })
.RequireAuthorization();

        // ===== 1. Démarrer la connexion =====
        group.MapGet("/login", (HttpContext context, IConfiguration config) =>
        {
            var verifier = PkceService.GenerateCodeVerifier();
            var challenge = PkceService.GenerateCodeChallenge(verifier);
            var state = PkceService.GenerateState();

            // On garde le verifier et le state pour l'étape callback
            context.Response.Cookies.Append("pkce_verifier", verifier, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(10)
            });

            context.Response.Cookies.Append("oauth_state", state, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(10)
            });

            var url = $"{config["Sso:Authority"]}/connect/authorize" +
                      $"?client_id={Uri.EscapeDataString(config["Sso:ClientId"]!)}" +
                      $"&redirect_uri={Uri.EscapeDataString(config["Sso:RedirectUri"]!)}" +
                      $"&response_type=code" +
                      $"&scope={Uri.EscapeDataString("openid profile email roles offline_access")}" +
                      $"&state={state}" +
                      $"&code_challenge={challenge}" +
                      $"&code_challenge_method=S256";
            return Results.Redirect(url);
        });

        // ===== 2. Recevoir le code et l'échanger =====
        group.MapGet("/callback", async (
            HttpContext context,
            IConfiguration config,
            IHttpClientFactory httpClientFactory,
            IConfigurationManager<OpenIdConnectConfiguration> oidcConfig,
            TokenStore tokenStore,
            ILogger<Program> logger,
            string? code,
            string? state,
            [FromQuery(Name = "error")] string? oauthError,
            [FromQuery(Name = "error_description")] string? oauthErrorDescription) =>
        {
            // SsoServer refuse l'autorisation (ex: aucun rôle assigné pour
            // cette application) : réponse standard OAuth2, pas un jeton.
            if (!string.IsNullOrEmpty(oauthError))
            {
                logger.LogWarning(
                    "Autorisation refusée par SsoServer : {Error} — {Description}",
                    oauthError, oauthErrorDescription);
                return Results.Json(
                    new { error = "Accès refusé.", detail = oauthErrorDescription },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (string.IsNullOrEmpty(code))
                return Results.BadRequest("Code manquant.");

            var savedState = context.Request.Cookies["oauth_state"];
            if (string.IsNullOrEmpty(savedState) || savedState != state)
                return Results.BadRequest("State invalide.");

            var verifier = context.Request.Cookies["pkce_verifier"];
            if (string.IsNullOrEmpty(verifier))
                return Results.BadRequest("Verifier introuvable.");

            // Échange serveur-à-serveur
            var client = httpClientFactory.CreateClient();

            var response = await client.PostAsync(
                $"{config["Sso:Authority"]}/connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = config["Sso:RedirectUri"]!,
                    ["client_id"] = config["Sso:ClientId"]!,
                    ["client_secret"] = config["Sso:ClientSecret"]!,
                    ["code_verifier"] = verifier
                }));

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return Results.BadRequest($"Échec de l'échange : {error}");
            }

            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (tokens is null)
                return Results.BadRequest("Réponse invalide.");

            // On valide l'id_token : signature, émetteur, audience et durée de
            // vie, avec les clés publiées par SsoServer sur son endpoint JWKS.
            // Un id_token qui échoue cette validation ne vient pas de notre
            // serveur ou a été altéré : ce n'est pas une erreur technique.
            var discovery = await oidcConfig.GetConfigurationAsync(context.RequestAborted);

            var validationParameters = new TokenValidationParameters
            {
                ValidIssuer = discovery.Issuer,
                ValidAudience = config["Sso:ClientId"],
                IssuerSigningKeys = discovery.SigningKeys,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };

            ClaimsPrincipal idTokenPrincipal;
            try
            {
                var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
                idTokenPrincipal = handler.ValidateToken(tokens.IdToken, validationParameters, out _);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "id_token invalide reçu sur /auth/callback.");
                return Results.BadRequest("Authentification invalide.");
            }

            var subject = idTokenPrincipal.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            if (string.IsNullOrEmpty(subject))
                return Results.BadRequest("id_token invalide : claim 'sub' manquant.");

            if (string.IsNullOrEmpty(tokens.RefreshToken))
                logger.LogWarning("Aucun refresh_token reçu sur /auth/callback (scope offline_access absent ou refusé) : le renouvellement sera impossible pour cette session.");

            var sessionId = TokenStore.NewSessionId();
            tokenStore.Set(sessionId, new StoredTokens
            {
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn)
            });

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, subject),
                new(ClaimTypes.Email, idTokenPrincipal.Claims.FirstOrDefault(c => c.Type == "email")?.Value ?? ""),
                new(ClaimTypes.Name, idTokenPrincipal.Claims.FirstOrDefault(c => c.Type == "name")?.Value ?? ""),
                new("session_id", sessionId)
            };

            foreach (var role in idTokenPrincipal.Claims.Where(c => c.Type == "role"))
                claims.Add(new Claim(ClaimTypes.Role, role.Value));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            // Nettoyage
            context.Response.Cookies.Delete("pkce_verifier");
            context.Response.Cookies.Delete("oauth_state");

            return Results.Redirect($"{config["Frontend:Url"]}/dashboard");
        });

        // ===== 3. Qui suis-je ? =====
        // Interroge /connect/userinfo avec l'access_token pour renvoyer des
        // informations fraîches plutôt que les claims figées au moment du
        // login. Repli sur les claims du cookie si l'appel échoue pour une
        // raison qui n'est pas l'expiration du jeton (le cookie de session,
        // lui, reste valide et ne doit pas être un point de fragilité).
        group.MapGet("/me", async (
            HttpContext context,
            ClaimsPrincipal user,
            IConfiguration config,
            IHttpClientFactory httpClientFactory,
            TokenRefreshService tokenRefresh,
            ILogger<Program> logger) =>
        {
            if (user.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            IResult FromCookieClaims() => Results.Ok(new
            {
                id = user.FindFirstValue(ClaimTypes.NameIdentifier),
                email = user.FindFirstValue(ClaimTypes.Email),
                name = user.FindFirstValue(ClaimTypes.Name),
                roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value)
            });

            // Le refresh_token est mort (révoqué, expiré, absent) : la session
            // OAuth est réellement terminée. On ferme aussi la session locale
            // pour ne pas laisser d'autres endpoints (cookie-only) continuer à
            // répondre comme si l'utilisateur était encore authentifié.
            async Task<IResult> ReauthRequired()
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Unauthorized();
            }

            var sessionId = user.FindFirstValue("session_id");
            if (sessionId is null)
                return FromCookieClaims();

            var refresh = await tokenRefresh.GetValidAccessTokenAsync(sessionId);

            if (refresh.Outcome == TokenRefreshOutcome.ReauthRequired)
                return await ReauthRequired();

            if (refresh.Outcome == TokenRefreshOutcome.TransientFailure || refresh.AccessToken is null)
                return FromCookieClaims();

            var accessToken = refresh.AccessToken;

            try
            {
                var client = httpClientFactory.CreateClient();
                var request = new HttpRequestMessage(
                    HttpMethod.Get, $"{config["Sso:Authority"]}/connect/userinfo");
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                var response = await client.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // access_token rejeté malgré le renouvellement proactif
                    // (fenêtre de course) : même traitement que ReauthRequired.
                    return await ReauthRequired();
                }

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Appel à /connect/userinfo échoué ({Status}), repli sur les claims du cookie.",
                        response.StatusCode);
                    return FromCookieClaims();
                }

                var info = await response.Content.ReadFromJsonAsync<UserInfoResponse>();
                if (info is null)
                    return FromCookieClaims();

                return Results.Ok(new
                {
                    id = info.Sub,
                    email = info.Email,
                    name = info.Name,
                    roles = info.Role ?? Array.Empty<string>()
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Impossible de joindre /connect/userinfo, repli sur les claims du cookie.");
                return FromCookieClaims();
            }
        });

        group.MapGet("/logout", async (
            HttpContext context,
            IConfiguration config,
            ClaimsPrincipal user,
            TokenStore tokenStore) =>
        {
            var sessionId = user.FindFirstValue("session_id");
            if (!string.IsNullOrEmpty(sessionId))
                tokenStore.Remove(sessionId);

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            var url = $"{config["Sso:Authority"]}/connect/logout" +
                      $"?client_id={Uri.EscapeDataString(config["Sso:ClientId"]!)}" +
                      $"&post_logout_redirect_uri={Uri.EscapeDataString(config["Frontend:Url"]!)}";

            return Results.Redirect(url);
        });
    }
}
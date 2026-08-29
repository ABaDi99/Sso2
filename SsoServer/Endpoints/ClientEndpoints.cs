using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SsoServer.Data;
using SsoServer.DTOs;
using SsoServer.Entities.Identity;
using SsoServer.Security;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SsoServer.Endpoints;

/// <summary>
/// Gestion des applications clientes OAuth à chaud.
///
/// OpenIddict stocke les applications en base : rien n'oblige à passer par
/// un seeder. Un client créé ici est utilisable immédiatement, sans
/// recompilation ni redémarrage.
/// </summary>
public static class ClientEndpoints
{
    public static void MapClientEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/api/clients")
                       .RequireAuthorization(policy => policy.RequireRole(AppRoles.Admin))
                       .AddEndpointFilter<RequireActiveAccountFilter>()
                       .AddEndpointFilter<RequireAdminHeaderFilter>();


        // ===== Lister =====
        group.MapGet("/", async (IOpenIddictApplicationManager manager) =>
        {
            var results = new List<ClientDto>();

            await foreach (var application in manager.ListAsync())
                results.Add(await ToDto(manager, application));

            return Results.Ok(results);
        });

        // ===== Détail =====
        group.MapGet("/{id}", async (string id, IOpenIddictApplicationManager manager) =>
        {
            var application = await manager.FindByIdAsync(id);

            return application is null
                ? Results.NotFound(new { error = $"Aucune application avec l'identifiant {id}." })
                : Results.Ok(await ToDto(manager, application));
        });

        // ===== Créer =====
        group.MapPost("/", async (
            CreateClientRequest request,
            IOpenIddictApplicationManager manager) =>
        {
            var problems = Validate(request.ClientId, request.ClientType,
                                    request.RedirectUris, request.PostLogoutRedirectUris);

            if (problems.Count > 0)
                return Results.BadRequest(new { errors = problems });

            if (await manager.FindByClientIdAsync(request.ClientId) is not null)
                return Results.Conflict(new
                {
                    error = $"Le client_id « {request.ClientId} » est déjà pris."
                });

            var confidential = request.ClientType.Equals(
                ClientTypes.Confidential, StringComparison.OrdinalIgnoreCase);

            // Généré ici, montré une seule fois. OpenIddict le hache avant
            // de l'écrire en base : il sera ensuite irrécupérable.
            var secret = confidential ? GenerateSecret() : null;

            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = request.ClientId,
                ClientSecret = secret,
                DisplayName = request.DisplayName ?? request.ClientId,
                ClientType = confidential ? ClientTypes.Confidential : ClientTypes.Public,
                ConsentType = ConsentTypes.Implicit
            };

            foreach (var uri in request.RedirectUris)
                descriptor.RedirectUris.Add(new Uri(uri));

            foreach (var uri in request.PostLogoutRedirectUris ?? [])
                descriptor.PostLogoutRedirectUris.Add(new Uri(uri));

            ApplyPermissions(descriptor, request.Scopes);

            await manager.CreateAsync(descriptor);

            var created = await manager.FindByClientIdAsync(request.ClientId);

            return Results.Created($"/admin/api/clients/{request.ClientId}",
                new ClientCreatedDto(
                    Client: await ToDto(manager, created!),
                    ClientSecret: secret,
                    Notice: confidential
                        ? "Copiez ce secret maintenant. Il ne sera plus jamais affiché."
                        : "Client public : aucun secret. La protection repose sur PKCE."));
        });

        // ===== Modifier =====
        group.MapPut("/{id}", async (
            string id,
            UpdateClientRequest request,
            IOpenIddictApplicationManager manager) =>
        {
            var application = await manager.FindByIdAsync(id);

            if (application is null)
                return Results.NotFound(new { error = $"Aucune application avec l'identifiant {id}." });

            var problems = Validate(null, null, request.RedirectUris, request.PostLogoutRedirectUris);

            if (problems.Count > 0)
                return Results.BadRequest(new { errors = problems });

            // Le bon cycle : on repart de l'état existant, on modifie, on
            // enregistre. Recréer l'application ferait perdre son secret.
            var descriptor = new OpenIddictApplicationDescriptor();
            await manager.PopulateAsync(descriptor, application);

            if (request.DisplayName is not null)
                descriptor.DisplayName = request.DisplayName;

            descriptor.RedirectUris.Clear();
            foreach (var uri in request.RedirectUris)
                descriptor.RedirectUris.Add(new Uri(uri));

            descriptor.PostLogoutRedirectUris.Clear();
            foreach (var uri in request.PostLogoutRedirectUris ?? [])
                descriptor.PostLogoutRedirectUris.Add(new Uri(uri));

            if (request.Scopes is not null)
                ApplyPermissions(descriptor, request.Scopes);

            await manager.UpdateAsync(application, descriptor);

            return Results.Ok(await ToDto(manager, application));
        });

        // ===== Rôles applicatifs assignés pour cette application =====
        // Vue inverse de /admin/api/users/{id}/app-roles : ici, pour une
        // application donnée, qui a quel rôle.
        group.MapGet("/{id}/app-roles", async (
            string id,
            IOpenIddictApplicationManager manager,
            ApplicationDbContext db,
            UserManager<ApplicationUser> users) =>
        {
            var application = await manager.FindByIdAsync(id);

            if (application is null)
                return Results.NotFound(new { error = $"Aucune application avec l'identifiant {id}." });

            var clientId = await manager.GetClientIdAsync(application);

            var assignments = await db.UserApplicationRoles
                .Where(x => x.ClientId == clientId)
                .Include(x => x.Role)
                .ToListAsync();

            var result = new List<ClientRoleAssignmentDto>();

            foreach (var a in assignments)
            {
                var user = await users.FindByIdAsync(a.UserId);
                result.Add(new ClientRoleAssignmentDto(
                    a.Id, a.UserId, user?.Email ?? a.UserId, a.Role.Name!));
            }

            return Results.Ok(result.OrderBy(x => x.UserEmail).ThenBy(x => x.RoleName));
        });

        // ===== Régénérer le secret =====
        group.MapPost("/{id}/rotate-secret", async (
            string id,
            IOpenIddictApplicationManager manager) =>
        {
            var application = await manager.FindByIdAsync(id);

            if (application is null)
                return Results.NotFound(new { error = $"Aucune application avec l'identifiant {id}." });

            var type = await manager.GetClientTypeAsync(application);

            if (!string.Equals(type, ClientTypes.Confidential, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new
                {
                    error = "Un client public n'a pas de secret à régénérer."
                });

            var descriptor = new OpenIddictApplicationDescriptor();
            await manager.PopulateAsync(descriptor, application);

            var secret = GenerateSecret();
            descriptor.ClientSecret = secret;

            await manager.UpdateAsync(application, descriptor);

            return Results.Ok(new
            {
                clientSecret = secret,
                notice = "Nouveau secret. L'ancien ne fonctionne plus : mettez à jour l'application concernée."
            });
        });

        // ===== Supprimer =====
        group.MapDelete("/{id}", async (
            string id,
            IOpenIddictApplicationManager manager) =>
        {
            var application = await manager.FindByIdAsync(id);

            if (application is null)
                return Results.NotFound(new { error = $"Aucune application avec l'identifiant {id}." });

            await manager.DeleteAsync(application);

            return Results.NoContent();
        });
    }

    // ================= Utilitaires =================

    private static string GenerateSecret()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                  .Replace("+", "-").Replace("/", "_").TrimEnd('=');

    private static List<string> Validate(
        string? clientId, string? clientType,
        string[]? redirectUris, string[]? postLogoutRedirectUris)
    {
        var problems = new List<string>();

        if (clientId is not null && string.IsNullOrWhiteSpace(clientId))
            problems.Add("client_id : obligatoire.");

        if (clientType is not null
            && !clientType.Equals(ClientTypes.Confidential, StringComparison.OrdinalIgnoreCase)
            && !clientType.Equals(ClientTypes.Public, StringComparison.OrdinalIgnoreCase))
            problems.Add("clientType : attendu « confidential » ou « public ».");

        if (redirectUris is null || redirectUris.Length == 0)
            problems.Add("redirectUris : au moins une adresse de retour est nécessaire.");
        else
            foreach (var uri in redirectUris)
                if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
                    problems.Add($"redirectUris : « {uri} » n'est pas une URL absolue valide.");

        foreach (var uri in postLogoutRedirectUris ?? [])
            if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
                problems.Add($"postLogoutRedirectUris : « {uri} » n'est pas une URL absolue valide.");

        return problems;
    }

    private static void ApplyPermissions(
        OpenIddictApplicationDescriptor descriptor, string[]? scopes)
    {
        descriptor.Permissions.Clear();

        descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.Endpoints.Logout);

        descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
        descriptor.Permissions.Add(Permissions.ResponseTypes.Code);

        foreach (var scope in scopes ?? ["openid", "profile", "email"])
        {
            // openid est implicite, offline_access se traduit par le
            // grant refresh_token : ni l'un ni l'autre n'est une permission de scope.
            if (scope.Equals("openid", StringComparison.OrdinalIgnoreCase))
                continue;

            if (scope.Equals("offline_access", StringComparison.OrdinalIgnoreCase))
            {
                descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
                continue;
            }

            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        // PKCE pour tout le monde : c'est la recommandation actuelle de l'IETF,
        // y compris pour les clients confidentiels.
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
    }

    private static async Task<ClientDto> ToDto(
        IOpenIddictApplicationManager manager, object application)
    {
        var type = await manager.GetClientTypeAsync(application) ?? "";

        return new ClientDto(
            Id: await manager.GetIdAsync(application) ?? "",
            ClientId: await manager.GetClientIdAsync(application) ?? "",
            DisplayName: await manager.GetDisplayNameAsync(application),
            ClientType: type,
            RedirectUris: [.. await manager.GetRedirectUrisAsync(application)],
            PostLogoutRedirectUris: [.. await manager.GetPostLogoutRedirectUrisAsync(application)],
            Permissions: [.. await manager.GetPermissionsAsync(application)],
            HasSecret: type.Equals(ClientTypes.Confidential, StringComparison.OrdinalIgnoreCase));
    }
}
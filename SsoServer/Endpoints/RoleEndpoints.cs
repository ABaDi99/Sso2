using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SsoServer.Data;
using SsoServer.DTOs;
using SsoServer.Entities.Identity;
using SsoServer.Security;

namespace SsoServer.Endpoints;

/// <summary>
/// Gestion des rôles. Un rôle appartient à une seule application
/// (ClientId), sauf "Admin" qui reste global — voir ApplicationRole.
/// </summary>
public static class RoleEndpoints
{
    public static void MapRoleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/api/roles")
                       .RequireAuthorization(policy => policy.RequireRole(AppRoles.Admin))
                       .AddEndpointFilter<RequireActiveAccountFilter>()
                       .AddEndpointFilter<RequireAdminHeaderFilter>();

        // ===== Lister =====
        // ?clientId= filtre sur les rôles d'une application précise (c'est
        // ce qu'utilise le sélecteur de rôle applicatif à attribuer) ; sans
        // filtre, la page Rôles montre tout, Admin compris.
        group.MapGet("/", async (
            RoleManager<ApplicationRole> roles,
            UserManager<ApplicationUser> users,
            ApplicationDbContext db,
            IOpenIddictApplicationManager manager,
            string? clientId) =>
        {
            var query = roles.Roles.AsQueryable();
            if (clientId is not null)
                query = query.Where(r => r.ClientId == clientId);

            var all = await query.OrderBy(r => r.Name).ToListAsync();

            var displayNames = new Dictionary<string, string>();
            var results = new List<RoleDto>();

            foreach (var role in all)
            {
                // GetUsersInRoleAsync ne connaît que les attributions natives
                // (AspNetUserRoles) : un rôle applicatif se compte via la
                // table UserApplicationRoles, pas via Identity.
                var memberCount = role.ClientId is null
                    ? (await users.GetUsersInRoleAsync(role.Name!)).Count
                    : await db.UserApplicationRoles.CountAsync(x => x.RoleId == role.Id);

                string? clientDisplayName = null;
                if (role.ClientId is not null)
                {
                    if (!displayNames.TryGetValue(role.ClientId, out clientDisplayName))
                    {
                        var appEntity = await manager.FindByClientIdAsync(role.ClientId);
                        clientDisplayName = appEntity is null
                            ? role.ClientId
                            : await manager.GetDisplayNameAsync(appEntity) ?? role.ClientId;
                        displayNames[role.ClientId] = clientDisplayName;
                    }
                }

                results.Add(new RoleDto(role.Id, role.Name!, role.ClientId, clientDisplayName, memberCount));
            }

            return Results.Ok(results);
        });

        // ===== Créer =====
        group.MapPost("/", async (
            CreateRoleRequest request,
            RoleManager<ApplicationRole> roles,
            IOpenIddictApplicationManager manager) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new RefusalDto("Le nom du rôle est obligatoire."));

            // La colonne AspNetRoles.NormalizedName est un nvarchar(256) : sans
            // ce contrôle, un nom trop long fait échouer l'insertion en base
            // et laisse fuiter une exception EF Core/SQL Server brute.
            if (request.Name.Length > 256)
                return Results.BadRequest(new RefusalDto("Le nom du rôle ne peut pas dépasser 256 caractères."));

            if (string.Equals(request.Name, AppRoles.Admin, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new RefusalDto(
                    "« Admin » est réservé au rôle global — choisissez un autre nom."));

            if (string.IsNullOrWhiteSpace(request.ClientId))
                return Results.BadRequest(new RefusalDto(
                    "Un rôle appartient à une application : le client_id est obligatoire."));

            var app = await manager.FindByClientIdAsync(request.ClientId);
            if (app is null)
                return Results.BadRequest(new RefusalDto(
                    $"Aucune application avec le client_id « {request.ClientId} »."));

            // RoleExistsAsync ignore ClientId : deux applications peuvent
            // avoir chacune un rôle du même nom, seule la combinaison
            // (nom, application) doit être unique.
            var duplicate = await roles.Roles.AnyAsync(r =>
                r.NormalizedName == request.Name.ToUpperInvariant() && r.ClientId == request.ClientId);

            if (duplicate)
                return Results.Conflict(new RefusalDto(
                    $"Le rôle « {request.Name} » existe déjà pour cette application."));

            var created = await roles.CreateAsync(new ApplicationRole(request.Name) { ClientId = request.ClientId });

            if (!created.Succeeded)
                return Results.BadRequest(new { errors = UserGuards.Describe(created) });

            var displayName = await manager.GetDisplayNameAsync(app) ?? request.ClientId;

            return Results.Created($"/admin/api/roles/{request.Name}",
                new RoleDto(request.Name, request.Name, request.ClientId, displayName, 0));
        });

        // ===== Supprimer =====
        // Par id, pas par nom : un nom ne suffit plus à identifier un rôle
        // de façon unique depuis que deux applications peuvent en partager un.
        group.MapDelete("/{id}", async (
            string id,
            RoleManager<ApplicationRole> roles,
            UserManager<ApplicationUser> users,
            ApplicationDbContext db) =>
        {
            var role = await roles.FindByIdAsync(id);

            if (role is null)
                return Results.NotFound(new RefusalDto("Ce rôle n'existe pas."));

            if (string.Equals(role.Name, AppRoles.Admin, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new RefusalDto(
                    "Le rôle Admin ne peut pas être supprimé : plus personne ne pourrait administrer le serveur."));

            var memberCount = role.ClientId is null
                ? (await users.GetUsersInRoleAsync(role.Name!)).Count
                : await db.UserApplicationRoles.CountAsync(x => x.RoleId == role.Id);

            if (memberCount > 0)
                return Results.BadRequest(new RefusalDto(
                    $"{memberCount} compte(s) portent encore ce rôle. Retirez-le-leur d'abord."));

            var deleted = await roles.DeleteAsync(role);

            return deleted.Succeeded
                ? Results.NoContent()
                : Results.BadRequest(new { errors = UserGuards.Describe(deleted) });
        });
    }
}

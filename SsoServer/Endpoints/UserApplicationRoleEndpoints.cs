using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SsoServer.Data;
using SsoServer.DTOs;
using SsoServer.Entities.Identity;
using SsoServer.Security;

namespace SsoServer.Endpoints;

/// <summary>
/// Rôles applicatifs d'un compte (par application cliente) — s'ajoutent
/// aux rôles globaux (voir UserEndpoints/RoleEndpoints), ne les remplacent
/// pas ; voir le commentaire sur l'union des rôles dans AuthorizationEndpoints.
/// </summary>
public static class UserApplicationRoleEndpoints
{
    public static void MapUserApplicationRoleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/api/users")
                       .RequireAuthorization(policy => policy.RequireRole(AppRoles.Admin))
                       .AddEndpointFilter<RequireActiveAccountFilter>()
                       .AddEndpointFilter<RequireAdminHeaderFilter>();

        group.MapGet("/{id}/app-roles", async (
            string id,
            UserManager<ApplicationUser> users,
            ApplicationDbContext db,
            IOpenIddictApplicationManager manager,
            CancellationToken ct) =>
        {
            var user = await users.FindByIdAsync(id);

            if (user is null)
                return Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."));

            var assignments = await db.UserApplicationRoles
                .AsNoTracking()
                .Where(x => x.UserId == id)
                .Include(x => x.Role)
                .ToListAsync(ct);

            // Un aller-retour par application distincte plutôt que par
            // assignation : une personne peut cumuler plusieurs rôles sur
            // la même application, ce qui redemandait la même application
            // autant de fois qu'elle a de rôles dessus.
            var displayNames = new Dictionary<string, string>();
            foreach (var clientId in assignments.Select(a => a.ClientId).Distinct())
            {
                var app = await manager.FindByClientIdAsync(clientId, ct);
                displayNames[clientId] = app is null
                    ? clientId
                    : await manager.GetDisplayNameAsync(app, ct) ?? clientId;
            }

            var result = new List<UserApplicationRoleDto>();

            foreach (var a in assignments)
            {
                var displayName = displayNames.TryGetValue(a.ClientId, out var name) ? name : a.ClientId;
                result.Add(new UserApplicationRoleDto(a.Id, a.ClientId, displayName, a.RoleId, a.Role.Name!));
            }

            return Results.Ok(result.OrderBy(x => x.ClientDisplayName).ThenBy(x => x.RoleName));
        });

        group.MapPost("/{id}/app-roles", async (
            string id,
            AssignApplicationRoleRequest request,
            UserManager<ApplicationUser> users,
            RoleManager<ApplicationRole> roles,
            ApplicationDbContext db,
            IOpenIddictApplicationManager manager) =>
        {
            var user = await users.FindByIdAsync(id);

            if (user is null)
                return Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."));

            // FindByClientIdAsync/FindByNameAsync lèvent une ArgumentNullException
            // sur une valeur manquante plutôt que de renvoyer null : sans ce
            // contrôle, un corps de requête incomplet fait planter l'appel en
            // 500 avec la trace complète au lieu d'un refus propre.
            if (string.IsNullOrWhiteSpace(request.ClientId))
                return Results.BadRequest(new RefusalDto("Le client_id est obligatoire."));

            if (string.IsNullOrWhiteSpace(request.RoleName))
                return Results.BadRequest(new RefusalDto("Le nom du rôle est obligatoire."));

            var app = await manager.FindByClientIdAsync(request.ClientId);

            if (app is null)
                return Results.BadRequest(new RefusalDto($"Aucune application avec le client_id « {request.ClientId} »."));

            // Un rôle appartient à une seule application : FindByNameAsync
            // ignorerait ClientId et pourrait renvoyer le "Manager" d'une
            // autre application au lieu de celui de request.ClientId, si
            // deux applications ont chacune un rôle du même nom.
            var role = await roles.Roles.FirstOrDefaultAsync(r =>
                r.Name == request.RoleName && r.ClientId == request.ClientId);

            if (role is null)
                return Results.BadRequest(new RefusalDto(
                    $"Le rôle « {request.RoleName} » n'existe pas pour cette application."));

            var exists = await db.UserApplicationRoles.AnyAsync(x =>
                x.UserId == id && x.ClientId == request.ClientId && x.RoleId == role.Id);

            if (exists)
                return Results.Conflict(new RefusalDto(
                    $"Ce compte a déjà le rôle « {request.RoleName} » pour cette application."));

            var assignment = new UserApplicationRole
            {
                UserId = id,
                ClientId = request.ClientId,
                RoleId = role.Id
            };

            db.UserApplicationRoles.Add(assignment);
            await db.SaveChangesAsync();

            var displayName = await manager.GetDisplayNameAsync(app) ?? request.ClientId;

            return Results.Created(
                $"/admin/api/users/{id}/app-roles/{assignment.Id}",
                new UserApplicationRoleDto(assignment.Id, request.ClientId, displayName, role.Id, role.Name!));
        });

        group.MapDelete("/{id}/app-roles/{appRoleId:int}", async (
            string id,
            int appRoleId,
            ApplicationDbContext db) =>
        {
            var assignment = await db.UserApplicationRoles
                .FirstOrDefaultAsync(x => x.Id == appRoleId && x.UserId == id);

            if (assignment is null)
                return Results.NotFound(new RefusalDto("Cette assignation n'existe pas."));

            db.UserApplicationRoles.Remove(assignment);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });
    }
}

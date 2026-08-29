using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SsoServer.DTOs;
using SsoServer.Entities.Identity;
using SsoServer.Security;

namespace SsoServer.Endpoints;

/// <summary>
/// Gestion des rôles globaux (catalogue partagé par le rôle Admin et par
/// les rôles applicatifs, voir UserApplicationRoleEndpoints).
/// </summary>
public static class RoleEndpoints
{
    public static void MapRoleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/api/roles")
                       .RequireAuthorization(policy => policy.RequireRole(AppRoles.Admin))
                       .AddEndpointFilter<RequireActiveAccountFilter>()
                       .AddEndpointFilter<RequireAdminHeaderFilter>();

        group.MapGet("/", async (
            RoleManager<IdentityRole> roles,
            UserManager<ApplicationUser> users) =>
        {
            var all = await roles.Roles.OrderBy(r => r.Name).ToListAsync();

            var results = new List<RoleDto>();

            foreach (var role in all)
            {
                var members = await users.GetUsersInRoleAsync(role.Name!);
                results.Add(new RoleDto(role.Id, role.Name!, members.Count));
            }

            return Results.Ok(results);
        });

        group.MapPost("/", async (CreateRoleRequest request, RoleManager<IdentityRole> roles) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new RefusalDto("Le nom du rôle est obligatoire."));

            // La colonne AspNetRoles.NormalizedName est un nvarchar(256) : sans
            // ce contrôle, un nom trop long fait échouer l'insertion en base
            // et laisse fuiter une exception EF Core/SQL Server brute.
            if (request.Name.Length > 256)
                return Results.BadRequest(new RefusalDto("Le nom du rôle ne peut pas dépasser 256 caractères."));

            if (await roles.RoleExistsAsync(request.Name))
                return Results.Conflict(new RefusalDto($"Le rôle « {request.Name} » existe déjà."));

            var created = await roles.CreateAsync(new IdentityRole(request.Name));

            return created.Succeeded
                ? Results.Created($"/admin/api/roles/{request.Name}", new { name = request.Name })
                : Results.BadRequest(new { errors = UserGuards.Describe(created) });
        });

        group.MapDelete("/{name}", async (
            string name,
            RoleManager<IdentityRole> roles,
            UserManager<ApplicationUser> users) =>
        {
            if (string.Equals(name, AppRoles.Admin, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new RefusalDto(
                    "Le rôle Admin ne peut pas être supprimé : plus personne ne pourrait administrer le serveur."));

            var role = await roles.FindByNameAsync(name);

            if (role is null)
                return Results.NotFound(new RefusalDto($"Le rôle « {name} » n'existe pas."));

            var members = await users.GetUsersInRoleAsync(name);

            if (members.Count > 0)
                return Results.BadRequest(new RefusalDto(
                    $"{members.Count} compte(s) portent encore ce rôle. Retirez-le-leur d'abord."));

            var deleted = await roles.DeleteAsync(role);

            return deleted.Succeeded
                ? Results.NoContent()
                : Results.BadRequest(new { errors = UserGuards.Describe(deleted) });
        });
    }
}

using ClientApi.Security;
using ClientApi.Services;

namespace ClientApi.Endpoints;

public sealed record RoleDto(string Name, string Description, string[] Permissions);
public sealed record CreateRoleRequest(string Name, string? Description);
public sealed record SetPermissionsRequest(string[] Permissions);

/// <summary>
/// Gestion des rôles et permissions *tels que définis par cette
/// application* — voir RolePermissionStore pour pourquoi ce mapping vit
/// ici plutôt que dans SsoServer.
/// </summary>
public static class RolePermissionEndpoints
{
    public static void MapRolePermissionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/roles").RequireAuthorization();

        group.MapGet("/", (RolePermissionStore store) =>
            Results.Ok(store.GetAll()
                .Select(r => new RoleDto(r.Name, r.Description, [.. r.Permissions]))
                .OrderBy(r => r.Name)));

        group.MapGet("/permissions-catalog", () =>
            Results.Ok(RolePermissionStore.Catalog));

        group.MapPost("/", (CreateRoleRequest request, RolePermissionStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "Le nom du rôle est obligatoire." });

            var created = store.TryCreate(request.Name.Trim(), request.Description?.Trim() ?? "", out var role);

            if (!created)
                return Results.Conflict(new { error = $"Le rôle « {request.Name} » existe déjà ici." });

            return Results.Created($"/roles/{role!.Name}", new RoleDto(role.Name, role.Description, [.. role.Permissions]));
        })
        .RequireAuthorization(policy => policy.RequireRole(AppRoles.Admin));

        group.MapPut("/{name}/permissions", (string name, SetPermissionsRequest request, RolePermissionStore store) =>
        {
            var updated = store.SetPermissions(name, request.Permissions);

            if (updated is null)
                return Results.NotFound(new { error = $"Le rôle « {name} » n'est pas défini ici." });

            return Results.Ok(new RoleDto(updated.Name, updated.Description, [.. updated.Permissions]));
        })
        .RequireAuthorization(policy => policy.RequireRole(AppRoles.Admin));

        group.MapDelete("/{name}", (string name, RolePermissionStore store) =>
            store.Delete(name)
                ? Results.NoContent()
                : Results.BadRequest(new { error = $"Impossible de supprimer « {name} » (rôle protégé ou inconnu)." }))
        .RequireAuthorization(policy => policy.RequireRole(AppRoles.Admin));
    }
}

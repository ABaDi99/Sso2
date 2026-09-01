using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.EntityFrameworkCore;
using SsoServer.Data;
using SsoServer.DTOs;
using SsoServer.Entities.Identity;
using SsoServer.Security;
using System.Security.Claims;

namespace SsoServer.Endpoints;

/// <summary>
/// Gestion des comptes : création, modification, rôles globaux,
/// désactivation/réactivation, suppression.
///
/// Les rôles applicatifs (par application cliente) sont dans
/// UserApplicationRoleEndpoints, les suspensions datées dans
/// UserSuspensionEndpoints, et les rôles globaux (catalogue) dans
/// RoleEndpoints — trois sous-domaines distincts qui vivaient ici avant
/// d'être extraits. Les garde-fous d'administration partagés (dernier
/// admin actif, auto-modification) sont dans Security.UserGuards.
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        MapUsers(app);
        app.MapUserApplicationRoleEndpoints();
        app.MapUserSuspensionEndpoints();
        app.MapRoleEndpoints();
    }

    private static void MapUsers(WebApplication app)
    {
        var group = app.MapGroup("/admin/api/users")
                       .RequireAuthorization(policy => policy.RequireRole(AppRoles.Admin))
                       .AddEndpointFilter<RequireActiveAccountFilter>()
                       .AddEndpointFilter<RequireAdminHeaderFilter>();

        // ===== Lister =====
        group.MapGet("/", async (
            UserManager<ApplicationUser> users,
            ApplicationDbContext db,
            string? search,
            string? roleId,
            CancellationToken ct,
            int page = 1,
            int pageSize = 20) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = users.Users.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToUpperInvariant();
                query = query.Where(u =>
                    u.NormalizedEmail!.Contains(term) ||
                    u.NormalizedUserName!.Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(roleId))
            {
                // Un rôle est soit global (attribution native Identity, table
                // AspNetUserRoles), soit applicatif (UserApplicationRoles) —
                // on ne sait pas lequel sans le lire, donc on cherche des deux
                // côtés : par construction, un roleId ne peut matcher que
                // l'un des deux.
                var matchingUserIds = await db.UserRoles
                    .AsNoTracking()
                    .Where(ur => ur.RoleId == roleId)
                    .Select(ur => ur.UserId)
                    .Union(db.UserApplicationRoles
                        .AsNoTracking()
                        // Écarte les assignations historiques dont le rôle
                        // n'appartient plus à l'application de l'assignation
                        // (voir le commentaire équivalent dans ClientEndpoints).
                        .Where(ur => ur.RoleId == roleId && ur.Role.ClientId == ur.ClientId)
                        .Select(ur => ur.UserId))
                    .ToListAsync(ct);

                query = query.Where(u => matchingUserIds.Contains(u.Id));
            }

            var total = await query.CountAsync(ct);

            var pagedUsers = await query
                .OrderBy(u => u.Email)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var items = await ToDtosAsync(db, pagedUsers, ct);

            return Results.Ok(new UserListDto([.. items], total, page, pageSize));
        });

        // ===== Détail =====
        group.MapGet("/{id}", async (string id, UserManager<ApplicationUser> users, ApplicationDbContext db) =>
        {
            var user = await users.FindByIdAsync(id);

            return user is null
                ? Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."))
                : Results.Ok(await ToDto(users, db, user));
        });

        // ===== Créer =====
        group.MapPost("/", async (
            CreateUserRequest request,
            UserManager<ApplicationUser> users,
            RoleManager<ApplicationRole> roles,
            ApplicationDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return Results.BadRequest(new RefusalDto("L'adresse électronique est obligatoire."));

            if (await users.FindByEmailAsync(request.Email) is not null)
                return Results.Conflict(new RefusalDto(
                    $"Un compte utilise déjà l'adresse {request.Email}."));

            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                PhoneNumber = request.PhoneNumber,
                EmailConfirmed = true,
                LockoutEnabled = true   // sans ça, la désactivation serait sans effet
            };

            var created = await users.CreateAsync(user, request.Password);

            if (!created.Succeeded)
                return Results.BadRequest(new { errors = UserGuards.Describe(created) });

            foreach (var role in request.Roles ?? [])
            {
                if (!await roles.RoleExistsAsync(role))
                    continue;

                await users.AddToRoleAsync(user, role);
            }

            return Results.Created($"/admin/api/users/{user.Id}", await ToDto(users, db, user));
        });

        // ===== Modifier =====
        group.MapPut("/{id}", async (
            string id,
            UpdateUserRequest request,
            UserManager<ApplicationUser> users,
            ApplicationDbContext db) =>
        {
            var user = await users.FindByIdAsync(id);

            if (user is null)
                return Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."));

            if (!string.IsNullOrWhiteSpace(request.Email) && request.Email != user.Email)
            {
                var taken = await users.FindByEmailAsync(request.Email);

                if (taken is not null && taken.Id != user.Id)
                    return Results.Conflict(new RefusalDto(
                        $"Un autre compte utilise déjà l'adresse {request.Email}."));

                user.Email = request.Email;
                user.UserName = request.Email;
            }

            if (request.PhoneNumber is not null)
                user.PhoneNumber = request.PhoneNumber;

            var updated = await users.UpdateAsync(user);

            return updated.Succeeded
                ? Results.Ok(await ToDto(users, db, user))
                : Results.BadRequest(new { errors = UserGuards.Describe(updated) });
        });

        // ===== Rôles globaux d'un compte =====
        group.MapPut("/{id}/roles", async (
            string id,
            SetRolesRequest request,
            ClaimsPrincipal current,
            UserManager<ApplicationUser> users,
            RoleManager<ApplicationRole> roles,
            ApplicationDbContext db,
            ILogger<Program> logger) =>
        {
            var user = await users.FindByIdAsync(id);

            if (user is null)
                return Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."));

            var wanted = request.Roles ?? [];

            // Un rôle appartient désormais à une application (voir
            // ApplicationRole.ClientId) ; le seul rôle réellement global
            // est Admin. Accepter un autre nom ici risquerait de tomber sur
            // plusieurs rôles partageant ce nom pour des applications
            // différentes — Identity (IsInRoleAsync, AddToRolesAsync...)
            // suppose un nom unique et échoue dans ce cas plutôt que de
            // choisir. Les rôles applicatifs se gèrent via
            // /admin/api/users/{id}/app-roles, pas ici.
            if (wanted.Any(r => !string.Equals(r, AppRoles.Admin, StringComparison.OrdinalIgnoreCase)))
                return Results.BadRequest(new RefusalDto(
                    "Seul le rôle Admin peut être attribué globalement. " +
                    "Les autres rôles s'attribuent par application (« Rôles applicatifs »)."));

            // Garde-fou : ne pas se retirer soi-même le rôle Admin.
            if (UserGuards.IsSelf(current, user)
                && await users.IsInRoleAsync(user, AppRoles.Admin)
                && !wanted.Contains(AppRoles.Admin, StringComparer.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new RefusalDto(
                    "Vous ne pouvez pas retirer votre propre rôle Admin. " +
                    "Demandez à un autre administrateur de le faire."));
            }

            // Garde-fou : conserver au moins un administrateur actif.
            if (await users.IsInRoleAsync(user, AppRoles.Admin)
                && !wanted.Contains(AppRoles.Admin, StringComparer.OrdinalIgnoreCase)
                && await UserGuards.IsActiveAdmin(db, user)
                && await UserGuards.CountActiveAdmins(users, db) <= 1)
            {
                return Results.BadRequest(new RefusalDto(
                    "C'est le dernier administrateur actif. Nommez-en un autre avant."));
            }

            foreach (var role in wanted)
                if (!await roles.RoleExistsAsync(role))
                    return Results.BadRequest(new RefusalDto($"Le rôle « {role} » n'existe pas."));

            var existing = await users.GetRolesAsync(user);

            var removed = await users.RemoveFromRolesAsync(user, existing.Except(wanted));
            if (!removed.Succeeded)
                return Results.BadRequest(new { errors = UserGuards.Describe(removed) });

            var added = await users.AddToRolesAsync(user, wanted.Except(existing));
            if (!added.Succeeded)
                return Results.BadRequest(new { errors = UserGuards.Describe(added) });

            logger.LogInformation(
                "Rôles globaux de {Email} modifiés par {Actor} : {Roles}.",
                user.Email, current.FindFirst(ClaimTypes.NameIdentifier)?.Value, string.Join(", ", wanted));

            return Results.Ok(await ToDto(users, db, user));
        });

        // ===== Réinitialiser le mot de passe =====
        group.MapPost("/{id}/reset-password", async (
            string id,
            SetPasswordRequest request,
            UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByIdAsync(id);

            if (user is null)
                return Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."));

            var token = await users.GeneratePasswordResetTokenAsync(user);
            var reset = await users.ResetPasswordAsync(user, token, request.NewPassword);

            return reset.Succeeded
                ? Results.Ok(new { success = true })
                : Results.BadRequest(new { errors = UserGuards.Describe(reset) });
        });

        // ===== Désactiver =====
        group.MapPost("/{id}/disable", async (
            string id,
            ClaimsPrincipal current,
            UserManager<ApplicationUser> users,
            ApplicationDbContext db,
            ILogger<Program> logger) =>
        {
            var user = await users.FindByIdAsync(id);

            if (user is null)
                return Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."));

            if (UserGuards.IsSelf(current, user))
                return Results.BadRequest(new RefusalDto(
                    "Vous ne pouvez pas désactiver votre propre compte."));

            if (await users.IsInRoleAsync(user, AppRoles.Admin)
                && await UserGuards.IsActiveAdmin(db, user)
                && await UserGuards.CountActiveAdmins(users, db) <= 1)
                return Results.BadRequest(new RefusalDto(
                    "C'est le dernier administrateur actif. Nommez-en un autre avant."));

            await users.SetLockoutEnabledAsync(user, true);
            var locked = await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

            if (!locked.Succeeded)
                return Results.BadRequest(new { errors = UserGuards.Describe(locked) });

            // Le verrouillage seul ne coupe pas les sessions déjà ouvertes :
            // le cookie n'est revalidé contre l'état en base qu'au SecurityStamp,
            // jamais contre LockoutEnd. On force donc son renouvellement.
            await users.UpdateSecurityStampAsync(user);

            logger.LogInformation(
                "Compte {Email} désactivé par {Actor}.",
                user.Email, current.FindFirst(ClaimTypes.NameIdentifier)?.Value);

            return Results.Ok(await ToDto(users, db, user));
        });

        // ===== Réactiver =====
        group.MapPost("/{id}/enable", async (
            string id, UserManager<ApplicationUser> users, ApplicationDbContext db) =>
        {
            var user = await users.FindByIdAsync(id);

            if (user is null)
                return Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."));

            var unlocked = await users.SetLockoutEndDateAsync(user, null);

            if (!unlocked.Succeeded)
                return Results.BadRequest(new { errors = UserGuards.Describe(unlocked) });

            // Remet à zéro le compteur d'échecs, sinon quelques tentatives
            // ratées reverrouilleraient le compte aussitôt.
            await users.ResetAccessFailedCountAsync(user);

            return Results.Ok(await ToDto(users, db, user));
        });

        // ===== Supprimer =====
        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal current,
            UserManager<ApplicationUser> users,
            ApplicationDbContext db,
            ILogger<Program> logger) =>
        {
            var user = await users.FindByIdAsync(id);

            if (user is null)
                return Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."));

            if (UserGuards.IsSelf(current, user))
                return Results.BadRequest(new RefusalDto(
                    "Vous ne pouvez pas supprimer votre propre compte."));

            if (await users.IsInRoleAsync(user, AppRoles.Admin)
                && await UserGuards.IsActiveAdmin(db, user)
                && await UserGuards.CountActiveAdmins(users, db) <= 1)
                return Results.BadRequest(new RefusalDto(
                    "C'est le dernier administrateur actif. Nommez-en un autre avant."));

            var deleted = await users.DeleteAsync(user);

            if (deleted.Succeeded)
                logger.LogInformation(
                    "Compte {Email} supprimé par {Actor}.",
                    user.Email, current.FindFirst(ClaimTypes.NameIdentifier)?.Value);

            return deleted.Succeeded
                ? Results.NoContent()
                : Results.BadRequest(new { errors = UserGuards.Describe(deleted) });
        });
    }

    // ==================== Construction des DTO ====================

    private static async Task<UserDto> ToDto(
        UserManager<ApplicationUser> users, ApplicationDbContext db, ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);
        var status = await AccountStatusChecker.CheckAsync(db, user);
        var appRoleCount = await db.UserApplicationRoles
            .CountAsync(ur => ur.UserId == user.Id && ur.Role.ClientId == ur.ClientId);

        return ToDto(user, roles, status, appRoleCount);
    }

    // Version "batch" pour /admin/api/users : la version au-dessus, appelée
    // en boucle, faisait deux allers-retours base de données par compte
    // (rôles + statut) — jusqu'à 200 requêtes pour une page de 100. Ici, un
    // seul aller-retour pour les rôles et un seul pour les suspensions
    // actives, quel que soit le nombre de comptes sur la page.
    private static async Task<List<UserDto>> ToDtosAsync(
        ApplicationDbContext db, List<ApplicationUser> pagedUsers, CancellationToken ct)
    {
        var userIds = pagedUsers.Select(u => u.Id).ToList();

        var rolesByUser = await db.UserRoles
            .AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(db.Roles.AsNoTracking(), ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var activeSuspensionUntilByUser = await db.UserSuspensions
            .AsNoTracking()
            .Where(s => userIds.Contains(s.UserId) && s.DateDebut <= now && now <= s.DateFin)
            // Le même utilisateur ne devrait pas avoir deux suspensions actives
            // en même temps (chevauchement refusé à la création), mais au cas
            // où, on garde celle qui bloque le plus longtemps.
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, Until = g.Max(s => s.DateFin) })
            .ToDictionaryAsync(x => x.UserId, x => x.Until, ct);

        var appRoleCountByUser = await db.UserApplicationRoles
            .AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId) && ur.Role.ClientId == ur.ClientId)
            .GroupBy(ur => ur.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var result = new List<UserDto>(pagedUsers.Count);
        foreach (var user in pagedUsers)
        {
            var roles = rolesByUser.Where(r => r.UserId == user.Id).Select(r => r.Name!).ToArray();
            var isSuspended = activeSuspensionUntilByUser.TryGetValue(user.Id, out var until);
            var status = new AccountBlockStatus(
                isSuspended ? AccountBlockReason.Suspended : AccountBlockReason.None,
                isSuspended ? until : null);
            var appRoleCount = appRoleCountByUser.GetValueOrDefault(user.Id);

            result.Add(ToDto(user, roles, status, appRoleCount));
        }

        return result;
    }

    private static UserDto ToDto(
        ApplicationUser user, IList<string> roles, AccountBlockStatus status, int appRoleCount) =>
        new(
            Id: user.Id,
            Email: user.Email,
            UserName: user.UserName,
            PhoneNumber: user.PhoneNumber,
            EmailConfirmed: user.EmailConfirmed,
            IsActive: user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow,
            IsSuspended: status.Reason == AccountBlockReason.Suspended,
            SuspendedUntil: status.Reason == AccountBlockReason.Suspended ? status.Until : null,
            Roles: [.. roles],
            AppRoleCount: appRoleCount);
}

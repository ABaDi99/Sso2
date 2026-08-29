using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SsoServer.Data;
using SsoServer.DTOs;
using SsoServer.Entities.Identity;
using SsoServer.Security;
using System.Security.Claims;

namespace SsoServer.Endpoints;

/// <summary>
/// Gestion des comptes et des rôles.
///
/// Trois garde-fous sont codés explicitement, parce qu'un système
/// d'identité dont plus personne ne détient les clés est irrécupérable
/// sans intervention en base :
///   1. un administrateur ne peut pas se supprimer lui-même
///   2. ni se désactiver, ni se retirer son propre rôle Admin
///   3. il doit toujours rester au moins un administrateur actif
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        MapUsers(app);
        MapRoles(app);
    }

    // ==================== UTILISATEURS ====================

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
            int page = 1,
            int pageSize = 20) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = users.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToUpperInvariant();
                query = query.Where(u =>
                    u.NormalizedEmail!.Contains(term) ||
                    u.NormalizedUserName!.Contains(term));
            }

            var total = await query.CountAsync();

            var pagedUsers = await query
                .OrderBy(u => u.Email)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = new List<UserDto>();
            foreach (var user in pagedUsers)
                items.Add(await ToDto(users, db, user));

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
            RoleManager<IdentityRole> roles,
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
                return Results.BadRequest(new { errors = Describe(created) });

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
                : Results.BadRequest(new { errors = Describe(updated) });
        });

        // ===== Rôles d'un compte =====
        group.MapPut("/{id}/roles", async (
            string id,
            SetRolesRequest request,
            ClaimsPrincipal current,
            UserManager<ApplicationUser> users,
            RoleManager<IdentityRole> roles,
            ApplicationDbContext db) =>
        {
            var user = await users.FindByIdAsync(id);

            if (user is null)
                return Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."));

            var wanted = request.Roles ?? [];

            // Garde-fou : ne pas se retirer soi-même le rôle Admin.
            if (IsSelf(current, user)
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
                && await IsActiveAdmin(db, user)
                && await CountActiveAdmins(users, db) <= 1)
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
                return Results.BadRequest(new { errors = Describe(removed) });

            var added = await users.AddToRolesAsync(user, wanted.Except(existing));
            if (!added.Succeeded)
                return Results.BadRequest(new { errors = Describe(added) });

            return Results.Ok(await ToDto(users, db, user));
        });

        // ===== Rôles applicatifs d'un compte (par application cliente) =====
        // S'ajoutent aux rôles globaux ci-dessus, ne les remplacent pas —
        // voir le commentaire sur l'union des rôles dans AuthorizationEndpoints.
        group.MapGet("/{id}/app-roles", async (
            string id,
            UserManager<ApplicationUser> users,
            ApplicationDbContext db,
            IOpenIddictApplicationManager manager) =>
        {
            var user = await users.FindByIdAsync(id);

            if (user is null)
                return Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."));

            var assignments = await db.UserApplicationRoles
                .Where(x => x.UserId == id)
                .Include(x => x.Role)
                .ToListAsync();

            var result = new List<UserApplicationRoleDto>();

            foreach (var a in assignments)
            {
                var app = await manager.FindByClientIdAsync(a.ClientId);
                var displayName = app is null ? a.ClientId : await manager.GetDisplayNameAsync(app) ?? a.ClientId;

                result.Add(new UserApplicationRoleDto(a.Id, a.ClientId, displayName, a.RoleId, a.Role.Name!));
            }

            return Results.Ok(result.OrderBy(x => x.ClientDisplayName).ThenBy(x => x.RoleName));
        });

        group.MapPost("/{id}/app-roles", async (
            string id,
            AssignApplicationRoleRequest request,
            UserManager<ApplicationUser> users,
            RoleManager<IdentityRole> roles,
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

            var role = await roles.FindByNameAsync(request.RoleName);

            if (role is null)
                return Results.BadRequest(new RefusalDto($"Le rôle « {request.RoleName} » n'existe pas."));

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
                : Results.BadRequest(new { errors = Describe(reset) });
        });

        // ===== Désactiver =====
        group.MapPost("/{id}/disable", async (
            string id,
            ClaimsPrincipal current,
            UserManager<ApplicationUser> users,
            ApplicationDbContext db) =>
        {
            var user = await users.FindByIdAsync(id);

            if (user is null)
                return Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."));

            if (IsSelf(current, user))
                return Results.BadRequest(new RefusalDto(
                    "Vous ne pouvez pas désactiver votre propre compte."));

            if (await users.IsInRoleAsync(user, AppRoles.Admin)
                && await IsActiveAdmin(db, user)
                && await CountActiveAdmins(users, db) <= 1)
                return Results.BadRequest(new RefusalDto(
                    "C'est le dernier administrateur actif. Nommez-en un autre avant."));

            await users.SetLockoutEnabledAsync(user, true);
            var locked = await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

            if (!locked.Succeeded)
                return Results.BadRequest(new { errors = Describe(locked) });

            // Le verrouillage seul ne coupe pas les sessions déjà ouvertes :
            // le cookie n'est revalidé contre l'état en base qu'au SecurityStamp,
            // jamais contre LockoutEnd. On force donc son renouvellement.
            await users.UpdateSecurityStampAsync(user);

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
                return Results.BadRequest(new { errors = Describe(unlocked) });

            // Remet à zéro le compteur d'échecs, sinon quelques tentatives
            // ratées reverrouilleraient le compte aussitôt.
            await users.ResetAccessFailedCountAsync(user);

            return Results.Ok(await ToDto(users, db, user));
        });

        // ===== Suspensions temporaires datées (congés, etc.) =====
        // Vérifiées à la volée (AccountStatusChecker), jamais matérialisées
        // dans LockoutEnd — voir la discussion sur ce choix de conception.
        group.MapGet("/{id}/suspensions", async (string id, ApplicationDbContext db) =>
        {
            var list = await db.UserSuspensions
                .Where(s => s.UserId == id)
                .OrderByDescending(s => s.DateDebut)
                .Select(s => new UserSuspensionDto(
                    s.Id, s.DateDebut, s.DateFin, s.Motif, s.Type.ToString(), s.CreatedBy, s.CreatedAt))
                .ToListAsync();

            return Results.Ok(list);
        });

        group.MapPost("/{id}/suspensions", async (
            string id,
            CreateSuspensionRequest request,
            ClaimsPrincipal current,
            UserManager<ApplicationUser> users,
            ApplicationDbContext db) =>
        {
            var user = await users.FindByIdAsync(id);

            if (user is null)
                return Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."));

            if (IsSelf(current, user))
                return Results.BadRequest(new RefusalDto(
                    "Vous ne pouvez pas vous suspendre vous-même."));

            if (request.DateFin <= request.DateDebut)
                return Results.BadRequest(new RefusalDto(
                    "La date de fin doit être postérieure à la date de début."));

            if (!Enum.TryParse<SuspensionType>(request.Type, ignoreCase: true, out var type))
                return Results.BadRequest(new RefusalDto(
                    $"Type de suspension inconnu : « {request.Type} »."));

            if (await users.IsInRoleAsync(user, AppRoles.Admin)
                && await IsActiveAdmin(db, user)
                && await CountActiveAdmins(users, db) <= 1)
                return Results.BadRequest(new RefusalDto(
                    "C'est le dernier administrateur actif. Nommez-en un autre avant."));

            // Chevauchement : deux périodes qui se recoupent pour le même
            // utilisateur rendraient ambiguë la question "jusqu'à quand ?".
            var overlap = await db.UserSuspensions.FirstOrDefaultAsync(s =>
                s.UserId == id && request.DateDebut <= s.DateFin && s.DateDebut <= request.DateFin);

            if (overlap is not null)
                return Results.Conflict(new RefusalDto(
                    $"Chevauche une période existante ({overlap.DateDebut:dd/MM/yyyy} → {overlap.DateFin:dd/MM/yyyy})."));

            var suspension = new UserSuspension
            {
                UserId = id,
                DateDebut = request.DateDebut,
                DateFin = request.DateFin,
                Motif = request.Motif,
                Type = type,
                CreatedBy = current.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "?",
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.UserSuspensions.Add(suspension);
            await db.SaveChangesAsync();

            return Results.Created(
                $"/admin/api/users/{id}/suspensions/{suspension.Id}",
                new UserSuspensionDto(suspension.Id, suspension.DateDebut, suspension.DateFin,
                    suspension.Motif, suspension.Type.ToString(), suspension.CreatedBy, suspension.CreatedAt));
        });

        group.MapPut("/{id}/suspensions/{suspensionId:int}", async (
            string id,
            int suspensionId,
            CreateSuspensionRequest request,
            ApplicationDbContext db) =>
        {
            var suspension = await db.UserSuspensions
                .FirstOrDefaultAsync(s => s.Id == suspensionId && s.UserId == id);

            if (suspension is null)
                return Results.NotFound(new RefusalDto("Cette période de suspension n'existe pas."));

            if (request.DateFin <= request.DateDebut)
                return Results.BadRequest(new RefusalDto(
                    "La date de fin doit être postérieure à la date de début."));

            if (!Enum.TryParse<SuspensionType>(request.Type, ignoreCase: true, out var type))
                return Results.BadRequest(new RefusalDto(
                    $"Type de suspension inconnu : « {request.Type} »."));

            var overlap = await db.UserSuspensions.FirstOrDefaultAsync(s =>
                s.UserId == id && s.Id != suspensionId &&
                request.DateDebut <= s.DateFin && s.DateDebut <= request.DateFin);

            if (overlap is not null)
                return Results.Conflict(new RefusalDto(
                    $"Chevauche une période existante ({overlap.DateDebut:dd/MM/yyyy} → {overlap.DateFin:dd/MM/yyyy})."));

            suspension.DateDebut = request.DateDebut;
            suspension.DateFin = request.DateFin;
            suspension.Motif = request.Motif;
            suspension.Type = type;

            await db.SaveChangesAsync();

            return Results.Ok(new UserSuspensionDto(suspension.Id, suspension.DateDebut, suspension.DateFin,
                suspension.Motif, suspension.Type.ToString(), suspension.CreatedBy, suspension.CreatedAt));
        });

        group.MapDelete("/{id}/suspensions/{suspensionId:int}", async (
            string id, int suspensionId, ApplicationDbContext db) =>
        {
            var suspension = await db.UserSuspensions
                .FirstOrDefaultAsync(s => s.Id == suspensionId && s.UserId == id);

            if (suspension is null)
                return Results.NotFound(new RefusalDto("Cette période de suspension n'existe pas."));

            db.UserSuspensions.Remove(suspension);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });

        // ===== Supprimer =====
        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal current,
            UserManager<ApplicationUser> users,
            ApplicationDbContext db) =>
        {
            var user = await users.FindByIdAsync(id);

            if (user is null)
                return Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."));

            if (IsSelf(current, user))
                return Results.BadRequest(new RefusalDto(
                    "Vous ne pouvez pas supprimer votre propre compte."));

            if (await users.IsInRoleAsync(user, AppRoles.Admin)
                && await IsActiveAdmin(db, user)
                && await CountActiveAdmins(users, db) <= 1)
                return Results.BadRequest(new RefusalDto(
                    "C'est le dernier administrateur actif. Nommez-en un autre avant."));

            var deleted = await users.DeleteAsync(user);

            return deleted.Succeeded
                ? Results.NoContent()
                : Results.BadRequest(new { errors = Describe(deleted) });
        });
    }

    // ==================== RÔLES ====================

    private static void MapRoles(WebApplication app)
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
                : Results.BadRequest(new { errors = Describe(created) });
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
                : Results.BadRequest(new { errors = Describe(deleted) });
        });
    }

    // ==================== Utilitaires ====================

    private static bool IsSelf(ClaimsPrincipal current, ApplicationUser user)
        => current.FindFirst(ClaimTypes.NameIdentifier)?.Value == user.Id;

    // Le compte cible est-il lui-même actif ? Si non (déjà suspendu ou
    // désactivé), agir dessus ne change rien au nombre d'admins actifs —
    // le garde-fou "dernier admin actif" ne doit alors pas s'appliquer,
    // sinon un admin déjà inactif devient impossible à nettoyer tant qu'il
    // est le seul autre admin du système.
    private static async Task<bool> IsActiveAdmin(ApplicationDbContext db, ApplicationUser user)
        => !(await AccountStatusChecker.CheckAsync(db, user)).IsBlocked;

    // Un admin actuellement suspendu ne compte pas comme "actif" : sinon,
    // suspendre le dernier admin non-désactivé laisserait la plateforme
    // sans personne capable de s'y connecter, malgré le garde-fou.
    private static async Task<int> CountActiveAdmins(UserManager<ApplicationUser> users, ApplicationDbContext db)
    {
        var admins = await users.GetUsersInRoleAsync(AppRoles.Admin);

        var count = 0;
        foreach (var admin in admins)
        {
            var status = await AccountStatusChecker.CheckAsync(db, admin);
            if (!status.IsBlocked)
                count++;
        }

        return count;
    }

    /// Les messages d'Identity sont précis ; on les remonte tels quels
    /// plutôt que de les remplacer par un « erreur » générique.
    private static string[] Describe(IdentityResult result)
        => [.. result.Errors.Select(e => e.Description)];

    private static async Task<UserDto> ToDto(
        UserManager<ApplicationUser> users, ApplicationDbContext db, ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);
        var status = await AccountStatusChecker.CheckAsync(db, user);

        return new UserDto(
            Id: user.Id,
            Email: user.Email,
            UserName: user.UserName,
            PhoneNumber: user.PhoneNumber,
            EmailConfirmed: user.EmailConfirmed,
            IsActive: user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow,
            IsSuspended: status.Reason == AccountBlockReason.Suspended,
            SuspendedUntil: status.Reason == AccountBlockReason.Suspended ? status.Until : null,
            Roles: [.. roles]);
    }
}
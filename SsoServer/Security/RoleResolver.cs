using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SsoServer.Data;
using SsoServer.Entities.Identity;

namespace SsoServer.Security;

/// <summary>
/// Rôles globaux et rôles applicatifs d'un utilisateur pour une application
/// donnée, gardés distincts (utile pour la liste blanche, qui traite le
/// rôle global Admin différemment d'un simple rôle applicatif) en plus de
/// leur union (utile pour construire un jeton).
/// </summary>
public sealed record EffectiveRoles(IReadOnlyList<string> GlobalRoles, IReadOnlyList<string> AppRoles)
{
    public IReadOnlyList<string> All => [.. GlobalRoles.Concat(AppRoles).Distinct()];
}

/// <summary>
/// Calcule les rôles effectifs d'un utilisateur pour une application
/// donnée : rôles globaux, plus rôles applicatifs assignés spécifiquement
/// pour ce client_id — union, jamais un remplacement (un rôle global comme
/// Admin doit continuer à fonctionner partout).
///
/// Ce calcul doit produire exactement le même résultat à l'émission du
/// jeton (/connect/authorize) et à la relecture (/connect/userinfo) : les
/// deux étaient auparavant dupliqués indépendamment, et avaient divergé une
/// première fois (/connect/userinfo omettait les rôles applicatifs).
/// </summary>
public static class RoleResolver
{
    public static async Task<EffectiveRoles> GetEffectiveRolesAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        ApplicationUser user,
        string? clientId,
        CancellationToken ct = default)
    {
        var globalRoles = await userManager.GetRolesAsync(user);

        var appRoles = clientId is null
            ? []
            : await db.UserApplicationRoles
                .AsNoTracking()
                .Where(x => x.UserId == user.Id && x.ClientId == clientId)
                .Select(x => x.Role.Name!)
                .ToListAsync(ct);

        return new EffectiveRoles([.. globalRoles], appRoles);
    }
}

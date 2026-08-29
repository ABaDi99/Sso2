using Microsoft.AspNetCore.Identity;
using SsoServer.Data;
using SsoServer.Entities.Identity;
using System.Security.Claims;

namespace SsoServer.Security;

/// <summary>
/// Garde-fous d'administration partagés entre les endpoints comptes,
/// rôles applicatifs et suspensions — extraits pour ne pas les recopier
/// dans chacun des fichiers qui les appliquent.
///
/// Trois garde-fous sont codés explicitement, parce qu'un système
/// d'identité dont plus personne ne détient les clés est irrécupérable
/// sans intervention en base :
///   1. un administrateur ne peut pas se supprimer lui-même
///   2. ni se désactiver, ni se retirer son propre rôle Admin
///   3. il doit toujours rester au moins un administrateur actif
/// </summary>
public static class UserGuards
{
    public static bool IsSelf(ClaimsPrincipal current, ApplicationUser user)
        => current.FindFirst(ClaimTypes.NameIdentifier)?.Value == user.Id;

    // Le compte cible est-il lui-même actif ? Si non (déjà suspendu ou
    // désactivé), agir dessus ne change rien au nombre d'admins actifs —
    // le garde-fou "dernier admin actif" ne doit alors pas s'appliquer,
    // sinon un admin déjà inactif devient impossible à nettoyer tant qu'il
    // est le seul autre admin du système.
    public static async Task<bool> IsActiveAdmin(ApplicationDbContext db, ApplicationUser user)
        => !(await AccountStatusChecker.CheckAsync(db, user)).IsBlocked;

    // Un admin actuellement suspendu ne compte pas comme "actif" : sinon,
    // suspendre le dernier admin non-désactivé laisserait la plateforme
    // sans personne capable de s'y connecter, malgré le garde-fou.
    public static async Task<int> CountActiveAdmins(UserManager<ApplicationUser> users, ApplicationDbContext db)
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
    public static string[] Describe(IdentityResult result)
        => [.. result.Errors.Select(e => e.Description)];
}

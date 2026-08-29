using Microsoft.EntityFrameworkCore;
using SsoServer.Data;
using SsoServer.Entities.Identity;

namespace SsoServer.Security;

public enum AccountBlockReason { None, Disabled, Suspended }

public record AccountBlockStatus(AccountBlockReason Reason, DateTimeOffset? Until)
{
    public bool IsBlocked => Reason != AccountBlockReason.None;

    // Message explicite mais sans détail interne : pas le motif de la
    // suspension (congé / disciplinaire), juste que le compte est bloqué
    // et jusqu'à quand, le cas échéant.
    public string? Message => Reason switch
    {
        AccountBlockReason.Disabled => "Ce compte a été désactivé.",
        AccountBlockReason.Suspended => $"Ce compte est suspendu jusqu'au {Until:dd/MM/yyyy}.",
        _ => null
    };
}

// Vérifié à la volée à chaque authentification (login, /connect/authorize,
// renouvellement de jeton) — jamais matérialisé dans LockoutEnd pour les
// suspensions, voir la discussion sur le choix "à la volée" vs tâche de fond.
public static class AccountStatusChecker
{
    public static async Task<AccountBlockStatus> CheckAsync(ApplicationDbContext db, ApplicationUser user)
    {
        if (user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow)
            return new AccountBlockStatus(AccountBlockReason.Disabled, null);

        var now = DateTimeOffset.UtcNow;

        var activeSuspension = await db.UserSuspensions
            .AsNoTracking()
            .Where(s => s.UserId == user.Id && s.DateDebut <= now && now <= s.DateFin)
            .OrderByDescending(s => s.DateFin)
            .FirstOrDefaultAsync();

        return activeSuspension is null
            ? new AccountBlockStatus(AccountBlockReason.None, null)
            : new AccountBlockStatus(AccountBlockReason.Suspended, activeSuspension.DateFin);
    }
}

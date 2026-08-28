namespace SsoServer.Entities.Identity;

public enum SuspensionType
{
    Conge,
    Disciplinaire,
    Autre
}

// Fenêtre de blocage datée (congé, suspension disciplinaire...), distincte
// de la désactivation permanente (LockoutEnd d'Identity). Une suspension
// est active si DateDebut <= maintenant <= DateFin — vérifié à la volée à
// chaque authentification, jamais matérialisé dans LockoutEnd.
public class UserSuspension
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;

    public DateTimeOffset DateDebut { get; set; }
    public DateTimeOffset DateFin { get; set; }
    public string Motif { get; set; } = null!;
    public SuspensionType Type { get; set; }

    public string CreatedBy { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

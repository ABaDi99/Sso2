namespace SsoServer.Entities.Identity;

// Assigne à un utilisateur, pour une application cliente OpenIddict
// précise (ClientId), un rôle qui — depuis l'introduction du champ
// ApplicationRole.ClientId — appartient déjà à cette même application.
// Le ClientId porté ici reste utile pour interroger "les rôles de cet
// utilisateur pour cette application" sans repasser par une jointure sur
// Role, mais doit toujours correspondre à Role.ClientId.
public class UserApplicationRole
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;

    // ClientId public d'OpenIddictApplications (ex: "mon-app-cliente"),
    // pas la clé interne — voir explication dans la conversation.
    public string ClientId { get; set; } = null!;

    public string RoleId { get; set; } = null!;
    public ApplicationRole Role { get; set; } = null!;
}

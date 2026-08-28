using Microsoft.AspNetCore.Identity;

namespace SsoServer.Entities.Identity;

// Un rôle (du catalogue global AspNetRoles) accordé à un utilisateur pour
// une application cliente OpenIddict précise (ClientId). S'ajoute aux
// rôles globaux de l'utilisateur, ne les remplace pas.
public class UserApplicationRole
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;

    // ClientId public d'OpenIddictApplications (ex: "mon-app-cliente"),
    // pas la clé interne — voir explication dans la conversation.
    public string ClientId { get; set; } = null!;

    public string RoleId { get; set; } = null!;
    public IdentityRole Role { get; set; } = null!;
}

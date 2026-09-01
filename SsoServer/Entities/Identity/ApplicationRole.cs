using Microsoft.AspNetCore.Identity;

namespace SsoServer.Entities.Identity;

/// <summary>
/// Un rôle appartient à une seule application (son ClientId OpenIddict),
/// sauf le rôle "Admin" qui reste global (ClientId nul) — c'est lui qui
/// donne accès au panneau d'administration et fonctionne partout.
///
/// Avant ce modèle, tous les rôles venaient d'un catalogue global partagé
/// entre applications ; seule l'assignation (UserApplicationRole) portait
/// le ClientId. Deux applications ne pouvaient donc pas avoir chacune leur
/// propre notion de "Manager" sans se marcher dessus.
/// </summary>
public class ApplicationRole : IdentityRole
{
    public string? ClientId { get; set; }

    public ApplicationRole() { }

    public ApplicationRole(string roleName) : base(roleName) { }
}

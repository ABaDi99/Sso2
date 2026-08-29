namespace ClientApi.Security;

/// <summary>
/// Noms des rôles utilisés côté ClientApi. Miroir de SsoServer.Security.AppRoles
/// (projets séparés, pas de bibliothèque partagée) — évite qu'"Admin" se
/// répète en chaîne littérale.
/// </summary>
public static class AppRoles
{
    public const string Admin = "Admin";
}

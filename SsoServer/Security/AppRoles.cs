namespace SsoServer.Security;

/// <summary>
/// Noms des rôles applicatifs. Source unique pour éviter que la chaîne
/// "Admin" se répète — et diverge un jour — dans plusieurs fichiers.
/// </summary>
public static class AppRoles
{
    public const string Admin = "Admin";
}

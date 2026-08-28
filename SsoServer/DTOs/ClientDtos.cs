namespace SsoServer.DTOs;

/// <summary>
/// Ce qu'on renvoie pour décrire une application cliente.
/// Le secret n'y figure pas : OpenIddict le hache en base et il est
/// définitivement illisible après création.
/// </summary>
public record ClientDto(
    string Id,
    string ClientId,
    string? DisplayName,
    string ClientType,
    string[] RedirectUris,
    string[] PostLogoutRedirectUris,
    string[] Permissions,
    bool HasSecret);

/// <summary>
/// Réponse de création : la seule et unique occasion de voir le secret.
/// </summary>
public record ClientCreatedDto(
    ClientDto Client,
    string? ClientSecret,
    string Notice);

public record CreateClientRequest(
    string ClientId,
    string? DisplayName,
    /// "confidential" (avec secret, pour un backend) ou "public" (SPA, sans secret)
    string ClientType,
    string[] RedirectUris,
    string[]? PostLogoutRedirectUris,
    /// Ex. ["openid", "profile", "email", "offline_access"]
    string[]? Scopes);

public record UpdateClientRequest(
    string? DisplayName,
    string[] RedirectUris,
    string[]? PostLogoutRedirectUris,
    string[]? Scopes);

/// Vue inverse de UserApplicationRoleDto : pour une application donnée,
/// qui a quel rôle — utilisée par l'écran de détail d'une application.
public record ClientRoleAssignmentDto(
    int Id,
    string UserId,
    string UserEmail,
    string RoleName);

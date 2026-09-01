namespace SsoServer.DTOs;

public record UserDto(
    string Id,
    string? Email,
    string? UserName,
    string? PhoneNumber,
    bool EmailConfirmed,
    /// Faux quand le compte est désactivé (LockoutEnd) : il ne peut plus se connecter.
    bool IsActive,
    /// Vrai quand une période de suspension couvre la date du jour — indépendant d'IsActive.
    bool IsSuspended,
    DateTimeOffset? SuspendedUntil,
    string[] Roles);

public record UserSuspensionDto(
    int Id,
    DateTimeOffset DateDebut,
    DateTimeOffset DateFin,
    string Motif,
    string Type,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public record CreateSuspensionRequest(
    DateTimeOffset DateDebut,
    DateTimeOffset DateFin,
    string Motif,
    string Type);

public record UserListDto(
    UserDto[] Items,
    int Total,
    int Page,
    int PageSize);

public record CreateUserRequest(
    string Email,
    string Password,
    string? PhoneNumber,
    string[]? Roles);

public record UpdateUserRequest(
    string? Email,
    string? PhoneNumber);

public record SetRolesRequest(string[] Roles);

public record SetPasswordRequest(string NewPassword);
/// <summary>
/// Réponse renvoyée quand une action est refusée par une règle métier
/// plutôt que par une erreur technique.
/// </summary>
public record RefusalDto(string Error);

/// ClientId nul = rôle global (uniquement Admin). ClientDisplayName est
/// résolu à partir de ClientId pour l'affichage, jamais stocké tel quel.
public record RoleDto(string Id, string Name, string? ClientId, string? ClientDisplayName, int UserCount);

public record CreateRoleRequest(string Name, string ClientId);

public record UserApplicationRoleDto(
    int Id,
    string ClientId,
    string ClientDisplayName,
    string RoleId,
    string RoleName);

public record AssignApplicationRoleRequest(string ClientId, string RoleName);
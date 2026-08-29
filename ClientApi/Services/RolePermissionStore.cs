namespace ClientApi.Services;

public sealed record PermissionDefinition(string Code, string Label);

public sealed record RoleDefinition(string Name, string Description, HashSet<string> Permissions);

/// <summary>
/// Ce que chaque rôle SSO signifie *pour cette application précise* :
/// SsoServer ne connaît que le nom du rôle ("Manager", "Employe"...), pas
/// ce qu'il autorise à faire ici — c'est délibérément à chaque application
/// cliente de le décider (voir sso-admin : "chaque application décide de
/// ce que ça permet chez elle"). Ce mapping vit donc entièrement côté
/// ClientApi, jamais dans SsoServer.
///
/// Stocké en mémoire, comme AnnouncementEndpoints — pas de base de données
/// dans ce projet de démonstration. Remis à l'état de départ (les deux
/// rôles seedés) à chaque redémarrage.
/// </summary>
public sealed class RolePermissionStore
{
    private readonly object _lock = new();

    public static readonly IReadOnlyList<PermissionDefinition> Catalog =
    [
        new("announcements.view", "Voir les annonces"),
        new("announcements.create", "Créer une annonce"),
        new("announcements.edit", "Modifier une annonce"),
        new("announcements.delete", "Supprimer une annonce"),
        new("roles.manage", "Gérer les rôles et permissions de cette application"),
    ];

    private readonly Dictionary<string, RoleDefinition> _roles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Admin"] = new("Admin", "Rôle global SSO — accès complet par convention.",
            [.. Catalog.Select(p => p.Code)]),
        ["Employe"] = new("Employe", "Rôle applicatif de démonstration seedé par SsoServer.",
            ["announcements.view"]),
    };

    public IReadOnlyList<RoleDefinition> GetAll()
    {
        lock (_lock)
            return [.. _roles.Values];
    }

    public RoleDefinition? Get(string name)
    {
        lock (_lock)
            return _roles.GetValueOrDefault(name);
    }

    public bool TryCreate(string name, string description, out RoleDefinition? created)
    {
        lock (_lock)
        {
            if (_roles.ContainsKey(name))
            {
                created = null;
                return false;
            }

            created = new RoleDefinition(name, description, []);
            _roles[name] = created;
            return true;
        }
    }

    public RoleDefinition? SetPermissions(string name, IEnumerable<string> permissions)
    {
        var validCodes = Catalog.Select(p => p.Code).ToHashSet();

        lock (_lock)
        {
            if (!_roles.TryGetValue(name, out var existing))
                return null;

            var updated = existing with { Permissions = permissions.Where(validCodes.Contains).ToHashSet() };
            _roles[name] = updated;
            return updated;
        }
    }

    public bool Delete(string name)
    {
        // Le rôle Admin de SsoServer existe toujours et donne accès à ce
        // panneau lui-même — le supprimer ici couperait la possibilité de
        // corriger l'erreur.
        if (string.Equals(name, "Admin", StringComparison.OrdinalIgnoreCase))
            return false;

        lock (_lock)
            return _roles.Remove(name);
    }

    /// Union des permissions de tous les rôles SSO courants de l'utilisateur,
    /// tels que connus par cette application — un rôle SSO absent de ce
    /// catalogue (jamais configuré ici) ne donne simplement aucune permission.
    public IReadOnlyList<string> ResolvePermissions(IEnumerable<string> ssoRoles)
    {
        lock (_lock)
        {
            return [.. ssoRoles
                .Select(r => _roles.GetValueOrDefault(r))
                .Where(r => r is not null)
                .SelectMany(r => r!.Permissions)
                .Distinct()];
        }
    }
}

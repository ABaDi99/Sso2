using ClientApi.Services;
using System.Security.Claims;

namespace ClientApi.Endpoints;

public record Announcement(int Id, string Title, string Content, string Author, DateTime CreatedAt);

public record AnnouncementRequest(string Title, string Content);

public static class AnnouncementEndpoints
{
    private static readonly object Lock = new();
    private static int _nextId = 3;

    private static readonly List<Announcement> Store =
    [
        new(1, "Bienvenue sur le portail interne",
            "Ce tableau d'annonces est réservé aux employés authentifiés via le SSO de l'entreprise.",
            "admin@entreprise.com", DateTime.UtcNow.AddDays(-2)),
        new(2, "Maintenance planifiée",
            "Une maintenance du serveur d'identité aura lieu ce week-end.",
            "admin@entreprise.com", DateTime.UtcNow.AddHours(-5)),
    ];

    // Ce que peut faire l'appelant sur les annonces, d'après les rôles SSO
    // de sa session courante et le mapping rôle → permissions défini pour
    // CETTE application (RolePermissionStore) — pas un simple RequireRole,
    // pour que la démonstration de permissions fines ait un effet réel côté
    // serveur, pas seulement sur l'affichage des boutons.
    private static bool HasPermission(ClaimsPrincipal user, RolePermissionStore store, string code)
    {
        var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value);
        return store.ResolvePermissions(roles).Contains(code);
    }

    public static void MapAnnouncementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/announcements").RequireAuthorization();

        // ===== Lister =====
        group.MapGet("/", (ClaimsPrincipal user, RolePermissionStore store) =>
        {
            if (!HasPermission(user, store, "announcements.view"))
                return Results.Forbid();

            lock (Lock)
                return Results.Ok(Store.OrderByDescending(a => a.CreatedAt).ToList());
        });

        // ===== Détail =====
        group.MapGet("/{id:int}", (int id, ClaimsPrincipal user, RolePermissionStore store) =>
        {
            if (!HasPermission(user, store, "announcements.view"))
                return Results.Forbid();

            lock (Lock)
            {
                var found = Store.FirstOrDefault(a => a.Id == id);
                return found is null ? Results.NotFound() : Results.Ok(found);
            }
        });

        // ===== Créer =====
        group.MapPost("/", (AnnouncementRequest request, ClaimsPrincipal user, RolePermissionStore store) =>
        {
            if (!HasPermission(user, store, "announcements.create"))
                return Results.Forbid();

            lock (Lock)
            {
                var announcement = new Announcement(
                    _nextId++,
                    request.Title,
                    request.Content,
                    user.FindFirstValue(ClaimTypes.Email) ?? "inconnu",
                    DateTime.UtcNow);

                Store.Add(announcement);
                return Results.Created($"/announcements/{announcement.Id}", announcement);
            }
        });

        // ===== Modifier =====
        group.MapPut("/{id:int}", (int id, AnnouncementRequest request, ClaimsPrincipal user, RolePermissionStore store) =>
        {
            if (!HasPermission(user, store, "announcements.edit"))
                return Results.Forbid();

            lock (Lock)
            {
                var index = Store.FindIndex(a => a.Id == id);
                if (index == -1)
                    return Results.NotFound();

                var updated = Store[index] with { Title = request.Title, Content = request.Content };
                Store[index] = updated;
                return Results.Ok(updated);
            }
        });

        // ===== Supprimer =====
        group.MapDelete("/{id:int}", (int id, ClaimsPrincipal user, RolePermissionStore store) =>
        {
            if (!HasPermission(user, store, "announcements.delete"))
                return Results.Forbid();

            lock (Lock)
            {
                var removed = Store.RemoveAll(a => a.Id == id);
                return removed > 0 ? Results.NoContent() : Results.NotFound();
            }
        });
    }
}

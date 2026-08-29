using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SsoServer.Data;
using SsoServer.DTOs;
using SsoServer.Entities.Identity;
using SsoServer.Security;
using System.Security.Claims;

namespace SsoServer.Endpoints;

/// <summary>
/// Suspensions temporaires datées (congés, mesures disciplinaires...) —
/// vérifiées à la volée (AccountStatusChecker), jamais matérialisées dans
/// LockoutEnd, voir la discussion sur ce choix de conception.
/// </summary>
public static class UserSuspensionEndpoints
{
    public static void MapUserSuspensionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/api/users")
                       .RequireAuthorization(policy => policy.RequireRole(AppRoles.Admin))
                       .AddEndpointFilter<RequireActiveAccountFilter>()
                       .AddEndpointFilter<RequireAdminHeaderFilter>();

        group.MapGet("/{id}/suspensions", async (string id, ApplicationDbContext db) =>
        {
            var list = await db.UserSuspensions
                .AsNoTracking()
                .Where(s => s.UserId == id)
                .OrderByDescending(s => s.DateDebut)
                .Select(s => new UserSuspensionDto(
                    s.Id, s.DateDebut, s.DateFin, s.Motif, s.Type.ToString(), s.CreatedBy, s.CreatedAt))
                .ToListAsync();

            return Results.Ok(list);
        });

        group.MapPost("/{id}/suspensions", async (
            string id,
            CreateSuspensionRequest request,
            ClaimsPrincipal current,
            UserManager<ApplicationUser> users,
            ApplicationDbContext db) =>
        {
            var user = await users.FindByIdAsync(id);

            if (user is null)
                return Results.NotFound(new RefusalDto($"Aucun compte avec l'identifiant {id}."));

            if (UserGuards.IsSelf(current, user))
                return Results.BadRequest(new RefusalDto(
                    "Vous ne pouvez pas vous suspendre vous-même."));

            if (request.DateFin <= request.DateDebut)
                return Results.BadRequest(new RefusalDto(
                    "La date de fin doit être postérieure à la date de début."));

            if (!Enum.TryParse<SuspensionType>(request.Type, ignoreCase: true, out var type))
                return Results.BadRequest(new RefusalDto(
                    $"Type de suspension inconnu : « {request.Type} »."));

            if (await users.IsInRoleAsync(user, AppRoles.Admin)
                && await UserGuards.IsActiveAdmin(db, user)
                && await UserGuards.CountActiveAdmins(users, db) <= 1)
                return Results.BadRequest(new RefusalDto(
                    "C'est le dernier administrateur actif. Nommez-en un autre avant."));

            // Chevauchement : deux périodes qui se recoupent pour le même
            // utilisateur rendraient ambiguë la question "jusqu'à quand ?".
            var overlap = await db.UserSuspensions.AsNoTracking().FirstOrDefaultAsync(s =>
                s.UserId == id && request.DateDebut <= s.DateFin && s.DateDebut <= request.DateFin);

            if (overlap is not null)
                return Results.Conflict(new RefusalDto(
                    $"Chevauche une période existante ({overlap.DateDebut:dd/MM/yyyy} → {overlap.DateFin:dd/MM/yyyy})."));

            var suspension = new UserSuspension
            {
                UserId = id,
                DateDebut = request.DateDebut,
                DateFin = request.DateFin,
                Motif = request.Motif,
                Type = type,
                CreatedBy = current.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "?",
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.UserSuspensions.Add(suspension);
            await db.SaveChangesAsync();

            return Results.Created(
                $"/admin/api/users/{id}/suspensions/{suspension.Id}",
                new UserSuspensionDto(suspension.Id, suspension.DateDebut, suspension.DateFin,
                    suspension.Motif, suspension.Type.ToString(), suspension.CreatedBy, suspension.CreatedAt));
        });

        group.MapPut("/{id}/suspensions/{suspensionId:int}", async (
            string id,
            int suspensionId,
            CreateSuspensionRequest request,
            ApplicationDbContext db) =>
        {
            var suspension = await db.UserSuspensions
                .FirstOrDefaultAsync(s => s.Id == suspensionId && s.UserId == id);

            if (suspension is null)
                return Results.NotFound(new RefusalDto("Cette période de suspension n'existe pas."));

            if (request.DateFin <= request.DateDebut)
                return Results.BadRequest(new RefusalDto(
                    "La date de fin doit être postérieure à la date de début."));

            if (!Enum.TryParse<SuspensionType>(request.Type, ignoreCase: true, out var type))
                return Results.BadRequest(new RefusalDto(
                    $"Type de suspension inconnu : « {request.Type} »."));

            var overlap = await db.UserSuspensions.AsNoTracking().FirstOrDefaultAsync(s =>
                s.UserId == id && s.Id != suspensionId &&
                request.DateDebut <= s.DateFin && s.DateDebut <= request.DateFin);

            if (overlap is not null)
                return Results.Conflict(new RefusalDto(
                    $"Chevauche une période existante ({overlap.DateDebut:dd/MM/yyyy} → {overlap.DateFin:dd/MM/yyyy})."));

            suspension.DateDebut = request.DateDebut;
            suspension.DateFin = request.DateFin;
            suspension.Motif = request.Motif;
            suspension.Type = type;

            await db.SaveChangesAsync();

            return Results.Ok(new UserSuspensionDto(suspension.Id, suspension.DateDebut, suspension.DateFin,
                suspension.Motif, suspension.Type.ToString(), suspension.CreatedBy, suspension.CreatedAt));
        });

        group.MapDelete("/{id}/suspensions/{suspensionId:int}", async (
            string id, int suspensionId, ApplicationDbContext db) =>
        {
            var suspension = await db.UserSuspensions
                .FirstOrDefaultAsync(s => s.Id == suspensionId && s.UserId == id);

            if (suspension is null)
                return Results.NotFound(new RefusalDto("Cette période de suspension n'existe pas."));

            db.UserSuspensions.Remove(suspension);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });
    }
}

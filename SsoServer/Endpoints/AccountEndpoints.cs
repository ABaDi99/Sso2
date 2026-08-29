using Microsoft.AspNetCore.Identity;
using SsoServer.Data;
using SsoServer.DTOs;
using SsoServer.Entities.Identity;
using SsoServer.Security;
using System.Security.Claims;

namespace SsoServer.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/account");




        // ===== Connexion =====
        // Utilisée par sso-admin pour se connecter directement au panneau
        // d'administration — un chemin distinct de /Account/Login (qui, lui,
        // sert le flow OAuth via /connect/authorize). Les deux doivent
        // appliquer le même contrôle de statut de compte.
        group.MapPost("/login", async (
            LoginRequest request,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext db,
            AccountLoginService loginService,
            ILogger<Program> logger) =>
        {
            var login = await loginService.AttemptLoginAsync(
                signInManager, userManager, db, request.Email, request.Password);

            switch (login.Outcome)
            {
                case LoginOutcome.Blocked:
                    logger.LogWarning("Connexion refusée pour {Email} : compte bloqué.", request.Email);
                    return Results.Json(new RefusalDto(login.Message!), statusCode: 423);

                case LoginOutcome.TemporarilyLocked:
                    logger.LogWarning(
                        "Connexion refusée pour {Email} : verrouillage temporaire après échecs répétés.",
                        request.Email);
                    return Results.Json(new RefusalDto(login.Message!), statusCode: 423);

                case LoginOutcome.InvalidCredentials:
                    logger.LogWarning("Connexion refusée pour {Email} : identifiants invalides.", request.Email);
                    return Results.Json(new RefusalDto(login.Message!), statusCode: 401);

                default:
                    return Results.Ok(new { success = true });
            }
        });

        // ===== État de la session =====
        group.MapGet("/session", (HttpContext context) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
                return Results.Ok(new { authenticated = false });

            return Results.Ok(new
            {
                authenticated = true,
                email = context.User.FindFirst(ClaimTypes.Email)?.Value
                        ?? context.User.Identity.Name,
                roles = context.User.FindAll(ClaimTypes.Role)
                                    .Select(c => c.Value)
                                    .ToArray()
            });
        });

        // ===== Déconnexion =====
        group.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Ok(new { success = true });
        });
    }
}
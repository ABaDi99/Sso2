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
            ApplicationDbContext db) =>
        {
            var candidate = await userManager.FindByEmailAsync(request.Email);
            if (candidate is not null)
            {
                var status = await AccountStatusChecker.CheckAsync(db, candidate);
                if (status.IsBlocked)
                    return Results.Json(new { error = status.Message }, statusCode: 423);
            }

            var result = await signInManager.PasswordSignInAsync(
                request.Email,
                request.Password,
                isPersistent: true,
                lockoutOnFailure: true);

            if (result.IsLockedOut)
                return Results.Json(
                    new { error = "Ce compte a été désactivé." },
                    statusCode: 423);

            if (!result.Succeeded)
                return Results.Json(
                    new { error = "Email ou mot de passe incorrect." },
                    statusCode: 401);

            return Results.Ok(new { success = true });
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
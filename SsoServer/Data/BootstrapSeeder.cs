using Microsoft.AspNetCore.Identity;
using SsoServer.Entities.Identity;
using SsoServer.Security;

namespace SsoServer.Data;

/// <summary>
/// Amorce ce qui ne peut pas être créé par l'interface d'administration :
/// le rôle Admin et le premier compte qui le porte. Sans lui, personne ne
/// pourrait se connecter à l'administration pour créer le premier admin.
///
/// Tout le reste — utilisateurs, rôles supplémentaires, applications OAuth —
/// se crée à chaud par l'API. Rien d'autre n'a sa place ici.
/// </summary>
public static class BootstrapSeeder
{
    public static async Task SeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var config = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>()
                                  .CreateLogger("Bootstrap");

        // ===== 1. Le rôle Admin =====
        if (!await roleManager.RoleExistsAsync(AppRoles.Admin))
        {
            var created = await roleManager.CreateAsync(new IdentityRole(AppRoles.Admin));

            if (!created.Succeeded)
            {
                logger.LogError("Création du rôle {Role} impossible : {Errors}",
                    AppRoles.Admin,
                    string.Join(" | ", created.Errors.Select(e => e.Description)));
                return;
            }

            logger.LogInformation("Rôle {Role} créé.", AppRoles.Admin);
        }
        else
        {
            logger.LogInformation("Rôle {Role} déjà présent.", AppRoles.Admin);
        }

        // ===== 2. Le compte administrateur =====
        var email = config["Bootstrap:AdminEmail"];
        var password = config["Bootstrap:AdminPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Bootstrap:AdminEmail ou Bootstrap:AdminPassword absent de la " +
                "configuration. Aucun compte administrateur n'a été créé.");
            return;
        }

        var admin = await userManager.FindByEmailAsync(email);

        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var created = await userManager.CreateAsync(admin, password);

            if (!created.Succeeded)
            {
                logger.LogError("Création du compte {Email} impossible : {Errors}",
                    email,
                    string.Join(" | ", created.Errors.Select(e => e.Description)));
                return;
            }

            logger.LogInformation("Compte administrateur {Email} créé.", email);
        }
        else
        {
            logger.LogInformation("Compte {Email} déjà présent.", email);
        }

        // ===== 3. L'attribution du rôle =====
        // Séparée de la création : si le compte existait déjà sans le rôle
        // — par exemple parce qu'un démarrage précédent avait échoué à
        // mi-parcours — on le rattrape ici.
        if (!await userManager.IsInRoleAsync(admin, AppRoles.Admin))
        {
            var assigned = await userManager.AddToRoleAsync(admin, AppRoles.Admin);

            if (!assigned.Succeeded)
            {
                logger.LogError("Attribution du rôle {Role} à {Email} impossible : {Errors}",
                    AppRoles.Admin, email,
                    string.Join(" | ", assigned.Errors.Select(e => e.Description)));
                return;
            }

            logger.LogInformation("Rôle {Role} attribué à {Email}.", AppRoles.Admin, email);
        }

        logger.LogInformation("Amorçage terminé.");
    }
}

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SsoServer.Entities.Identity;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SsoServer.Data;

/// <summary>
/// Recrée l'environnement de développement à chaque démarrage.
///
/// Ne s'exécute qu'en Development. Les applications réelles se déclarent
/// par l'interface d'administration ; celles-ci sont des fixtures de
/// développement, versionnées avec le code pour que le projet reparte à
/// l'identique après un clone ou une base recréée.
///
/// L'hôte vient de la configuration (Network:Host). Passer de localhost
/// au réseau local ne demande donc qu'un changement dans appsettings,
/// jamais une modification de ce fichier.
/// </summary>
public static class DevClientSeeder
{
    public static async Task SeedAsync(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            return;

        using var scope = app.Services.CreateScope();

        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                                           .CreateLogger("DevClients");

        // localhost par défaut : le développement solo continue de marcher
        // sans configuration particulière.
        var host = config["Network:Host"] ?? "localhost";
        var binome = config["Network:BinomeHost"];

        logger.LogInformation("Clients de développement — hôte : {Host}", host);

        await Upsert(manager, logger, new DevClient(
            ClientId: "mon-app-cliente",
            ClientSecret: "secret-de-test-123",
            DisplayName: "Application cliente de démonstration",
            RedirectUris: [$"http://{host}:5200/auth/callback"],
            PostLogout: [$"http://{host}:5173"]));

        await Upsert(manager, logger, new DevClient(
            ClientId: "postman-test",
            ClientSecret: "postman-secret-456",
            DisplayName: "Postman",
            RedirectUris: ["https://oauth.pstmn.io/v1/callback"],
            PostLogout: []));

        // Application du binôme : tourne sur un autre poste du réseau,
        // d'où une adresse distincte de Network:Host.
        if (!string.IsNullOrWhiteSpace(binome))
        {
            await Upsert(manager, logger, new DevClient(
                ClientId: "app-binome",
                ClientSecret: "binome-secret-789",
                DisplayName: "Application du binôme",
                RedirectUris: [$"http://{binome}:5200/auth/callback"],
                PostLogout: [$"http://{binome}:5173"]));
        }

        await SeedUsersAsync(scope.ServiceProvider, logger);

        logger.LogInformation("Clients de développement à jour.");
    }

    private sealed record DevClient(
        string ClientId,
        string ClientSecret,
        string DisplayName,
        string[] RedirectUris,
        string[] PostLogout);

    private static async Task Upsert(
        IOpenIddictApplicationManager manager,
        ILogger logger,
        DevClient client)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = client.ClientId,
            ClientSecret = client.ClientSecret,
            DisplayName = client.DisplayName,
            ClientType = ClientTypes.Confidential,
            ConsentType = ConsentTypes.Implicit,

            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Logout,

                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,

                Permissions.ResponseTypes.Code,

                Permissions.Scopes.Profile,
                Permissions.Scopes.Email,
                Permissions.Scopes.Roles,
                Permissions.Prefixes.Scope + Scopes.OfflineAccess,
            },

            Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange
            }
        };

        foreach (var uri in client.RedirectUris)
            descriptor.RedirectUris.Add(new Uri(uri));

        foreach (var uri in client.PostLogout)
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri));

        var existing = await manager.FindByClientIdAsync(client.ClientId);

        if (existing is null)
        {
            await manager.CreateAsync(descriptor);
            logger.LogInformation("  {ClientId} créé → {Uri}",
                client.ClientId, string.Join(", ", client.RedirectUris));
            return;
        }

        // Mise à jour, et non re-création : c'est ce qui permet de changer
        // les adresses de retour sans repartir d'une base vide.
        await manager.UpdateAsync(existing, descriptor);

        logger.LogInformation("  {ClientId} mis à jour → {Uri}",
            client.ClientId, string.Join(", ", client.RedirectUris));
    }

    // Classes (pas records) : le binder de configuration .NET lie de façon
    // fiable des propriétés mutables ; les records à constructeur
    // positionnel, une fois imbriqués (tableau d'objets dans un tableau),
    // reviennent silencieusement vides — pas d'erreur, juste un tableau nul.
    private sealed class DevUser
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public string[]? Roles { get; set; }
        public DevAppRole[]? AppRoles { get; set; }
    }

    private sealed class DevAppRole
    {
        public string ClientId { get; set; } = "";
        public string Role { get; set; } = "";
    }

    private static async Task SeedUsersAsync(IServiceProvider services, ILogger logger)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var db = services.GetRequiredService<ApplicationDbContext>();
        var config = services.GetRequiredService<IConfiguration>();

        var wanted = config.GetSection("DevUsers").Get<DevUser[]>() ?? [];

        foreach (var entry in wanted)
        {
            var user = await users.FindByEmailAsync(entry.Email);

            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = entry.Email,
                    Email = entry.Email,
                    EmailConfirmed = true,
                    LockoutEnabled = true
                };

                // On ne réécrit le mot de passe que d'un compte tout juste
                // créé — jamais d'un compte existant, quelqu'un a pu le
                // changer volontairement.
                var created = await users.CreateAsync(user, entry.Password);

                if (!created.Succeeded)
                {
                    logger.LogError("  {Email} — création impossible : {Errors}",
                        entry.Email,
                        string.Join(" | ", created.Errors.Select(e => e.Description)));
                    continue;
                }

                logger.LogInformation("  {Email} créé.", entry.Email);
            }
            else
            {
                logger.LogInformation("  {Email} déjà présent.", entry.Email);
            }

            // Rôles globaux — rare : Admin se gère via BootstrapSeeder, pas
            // ici. La plupart des DevUsers n'en ont aucun.
            foreach (var role in entry.Roles ?? [])
            {
                if (!await roles.RoleExistsAsync(role))
                    await roles.CreateAsync(new IdentityRole(role));

                if (!await users.IsInRoleAsync(user, role))
                    await users.AddToRoleAsync(user, role);
            }

            // Nettoyage : "Employe" était auparavant un rôle global (avant
            // l'introduction des rôles par application) — un compte plus
            // ancien peut encore le porter. On le retire une fois pour
            // toutes ; l'accès équivalent passe désormais par AppRoles
            // ci-dessous, propre à chaque application.
            if (await users.IsInRoleAsync(user, "Employe"))
                await users.RemoveFromRoleAsync(user, "Employe");

            // Rôles applicatifs — idempotent, s'applique aussi à un compte
            // déjà existant (ex : recréé avant l'introduction de la liste
            // blanche par application sur /connect/authorize).
            foreach (var appRole in entry.AppRoles ?? [])
            {
                if (!await roles.RoleExistsAsync(appRole.Role))
                    await roles.CreateAsync(new IdentityRole(appRole.Role));

                var role = await roles.FindByNameAsync(appRole.Role);

                var exists = await db.UserApplicationRoles.AnyAsync(x =>
                    x.UserId == user.Id && x.ClientId == appRole.ClientId && x.RoleId == role!.Id);

                if (!exists)
                {
                    db.UserApplicationRoles.Add(new UserApplicationRole
                    {
                        UserId = user.Id,
                        ClientId = appRole.ClientId,
                        RoleId = role!.Id
                    });
                    await db.SaveChangesAsync();
                }
            }
        }
    }
}
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SsoServer.Data;
using SsoServer.Endpoints;
using SsoServer.Entities.Identity;
using SsoServer.Security;

var builder = WebApplication.CreateBuilder(args);

// ===== PHASE 1 =====

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.UseOpenIddict();
});

// Sans état propre (délègue tout à SignInManager/UserManager/DbContext,
// eux-mêmes scoped) : peut être singleton ou scoped indifféremment, mais
// scoped reste cohérent avec le cycle de vie de ses dépendances.
builder.Services.AddScoped<AccountLoginService>();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";

    options.Cookie.Name = "Sso.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    // Le comportement par défaut du cookie Identity — rediriger vers la
    // page de login HTML — a du sens pour /Account/*, mais casse une API
    // JSON : côté sso-admin, un fetch() suit la redirection en silence et
    // se retrouve avec du HTML au lieu d'un vrai 401/403, donc aucune
    // erreur n'est détectée. On garde le comportement HTML partout ailleurs,
    // et on répond en JSON pour /admin/api/*.
    var defaultRedirectToLogin = options.Events.OnRedirectToLogin;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/admin/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        return defaultRedirectToLogin(context);
    };

    var defaultRedirectToAccessDenied = options.Events.OnRedirectToAccessDenied;
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/admin/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        return defaultRedirectToAccessDenied(context);
    };
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("LoginFront", policy =>
    {
        var origins = new List<string> { "http://localhost:5174" };

        var networkHost = builder.Configuration["Network:Host"];
        if (!string.IsNullOrWhiteSpace(networkHost))
            origins.Add($"http://{networkHost}:5174");

        var binomeHost = builder.Configuration["Network:BinomeHost"];
        if (!string.IsNullOrWhiteSpace(binomeHost))
            origins.Add($"http://{binomeHost}:5174");

        policy.WithOrigins([.. origins])
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddAuthorization();

builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<ApplicationDbContext>();
    })
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("connect/authorize")
               .SetTokenEndpointUris("connect/token")
               .SetLogoutEndpointUris("connect/logout")
               .SetUserinfoEndpointUris("connect/userinfo");

        options.AllowAuthorizationCodeFlow()
               .AllowRefreshTokenFlow()
               .RequireProofKeyForCodeExchange();

        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            "roles",
            OpenIddictConstants.Scopes.OfflineAccess);
        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();

        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableLogoutEndpointPassthrough()
               .EnableUserinfoEndpointPassthrough()
               .DisableTransportSecurityRequirement();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddRazorPages();

var app = builder.Build();

// Le serveur de dev Vite de sso-admin proxifie /Account, /api, /connect
// vers ce serveur en réécrivant le Host header vers sa propre cible
// (localhost:5171), et transmet l'hôte d'origine via X-Forwarded-Host
// (voir sso-admin/vite.config.ts). Sans ce middleware, les redirections
// construites par ASP.NET Core (ex: le Challenge du cookie Identity vers
// /Account/Login) utiliseraient toujours "localhost", cassé pour qui
// accède depuis une autre machine du réseau. Les en-têtes ne viennent que
// du proxy local (loopback), déjà couvert par KnownNetworks par défaut.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto
});

app.UseStaticFiles();
// ===== PHASE 2 =====

app.UseRouting();
app.UseCors("LoginFront");
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.MapAuthorizationEndpoints();
app.MapAccountEndpoints();
app.MapClientEndpoints();
app.MapUserEndpoints();

app.MapFallbackToFile("/admin/{*path}", "admin/index.html");
await BootstrapSeeder.SeedAsync(app);
await DevClientSeeder.SeedAsync(app);

app.Run();
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SsoServer.Data;
using SsoServer.Endpoints;
using SsoServer.Entities.Identity;

var builder = WebApplication.CreateBuilder(args);

// ===== PHASE 1 =====

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.UseOpenIddict();
});

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

        options.RegisterScopes("openid", "profile", "email", "roles", "offline_access");
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
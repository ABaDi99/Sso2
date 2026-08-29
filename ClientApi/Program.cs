using ClientApi.Endpoints;
using ClientApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// ===== PHASE 1 =====

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ClientApi.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);

        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddHttpClient();

// Stocke access_token/refresh_token côté serveur ; le cookie de session
// ne porte qu'une clé opaque vers cette entrée (voir TokenStore).
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddSingleton<TokenRefreshService>();

// Ce que chaque rôle SSO autorise à faire dans *cette* application —
// voir RolePermissionStore.
builder.Services.AddSingleton<RolePermissionStore>();

// Récupère et met en cache le document de découverte OIDC de SsoServer
// (issuer + clés de signature JWKS), pour valider les id_token reçus.
// Singleton : le ConfigurationManager rafraîchit lui-même son cache selon
// ses règles internes ; en créer un par requête irait rechercher les clés
// à chaque connexion.
builder.Services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(sp =>
{
    var authority = sp.GetRequiredService<IConfiguration>()["Sso:Authority"]!;

    return new ConfigurationManager<OpenIdConnectConfiguration>(
        $"{authority}/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever
        {
            // SsoServer tourne en HTTP sur le réseau local pour l'instant.
            // À retirer dès que le TLS est en place.
            RequireHttps = false
        });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(builder.Configuration["Frontend:Url"]!)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ===== PHASE 2 =====

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();
app.MapAnnouncementEndpoints();
app.MapRolePermissionEndpoints();

app.Run();
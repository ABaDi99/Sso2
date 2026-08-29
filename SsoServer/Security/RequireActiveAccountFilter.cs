using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using SsoServer.Data;
using SsoServer.DTOs;
using SsoServer.Entities.Identity;
using System.Security.Claims;

namespace SsoServer.Security;

/// <summary>
/// Revalide le statut du compte (désactivé/suspendu) à chaque appel au
/// panneau d'administration, pas seulement à la connexion.
///
/// Sans ce filtre, un admin suspendu ou désactivé après coup conserve un
/// accès complet à /admin/api/* tant que son cookie reste valide — le
/// contrôle par SecurityStamp d'ASP.NET Identity ne revérifie qu'à
/// intervalles espacés (30 minutes par défaut), et RequireRole(Admin) ne
/// regarde que les claims du cookie, jamais l'état actuel en base.
/// </summary>
public sealed class RequireActiveAccountFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (userId is not null)
        {
            var users = httpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var db = httpContext.RequestServices.GetRequiredService<ApplicationDbContext>();

            var user = await users.FindByIdAsync(userId);
            var status = user is null
                ? new AccountBlockStatus(AccountBlockReason.Disabled, null)
                : await AccountStatusChecker.CheckAsync(db, user);

            if (status.IsBlocked)
            {
                var logger = httpContext.RequestServices.GetRequiredService<ILogger<RequireActiveAccountFilter>>();
                logger.LogWarning(
                    "Accès admin refusé pour {Email} : compte bloqué ({Reason}).",
                    user?.Email ?? userId, status.Reason);

                await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                return Results.Json(
                    new RefusalDto(status.Message ?? "Ce compte est bloqué."),
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        return await next(context);
    }
}

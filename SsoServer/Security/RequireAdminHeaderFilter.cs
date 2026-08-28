
using Microsoft.AspNetCore.Http;

namespace SsoServer.Security;

/// <summary>
/// Deuxième couche anti-CSRF pour les API protégées par cookie.
///
/// Le cookie Identity est en SameSite=Lax, ce qui bloque déjà le cas
/// classique : un formulaire hébergé ailleurs qui poste vers cette API
/// n'emporte pas le cookie. Ce filtre couvre ce que Lax laisse passer —
/// les navigateurs anciens, et les origines d'un même site (sous-domaines).
///
/// Le principe : exiger un en-tête que seul un appelant légitime peut poser.
/// Un formulaire HTML ne sait pas ajouter d'en-tête personnalisé, et un
/// fetch cross-origin qui en ajoute déclenche un préflight CORS, refusé
/// puisque l'origine de l'attaquant n'est pas autorisée.
///
/// À noter : cela protège des attaques passant par un navigateur. Un appel
/// direct depuis un outil comme Postman n'est pas concerné — mais il
/// suppose que l'attaquant possède déjà le cookie, ce qui est un autre
/// problème.
/// </summary>
public sealed class RequireAdminHeaderFilter : IEndpointFilter
{
    public const string HeaderName = "X-Sso-Admin";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;

        // Les méthodes sûres ne modifient rien : inutile de les contraindre.
        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method))
        {
            return await next(context);
        }

        if (!request.Headers.ContainsKey(HeaderName))
        {
            return Results.Json(
                new
                {
                    error = $"En-tête {HeaderName} absent. Les requêtes qui modifient " +
                            "des données doivent le porter."
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}
using System.Collections.Concurrent;

namespace ClientApi.Services;

public enum TokenRefreshOutcome
{
    // Jeton déjà valide, ou renouvellement réussi.
    Valid,

    // refresh_token absent, invalide, révoqué ou expiré : fin légitime de
    // session, pas une erreur technique. L'appelant doit renvoyer
    // l'utilisateur vers une ré-authentification.
    ReauthRequired,

    // Échec réseau/technique lors du renouvellement : on ne sait pas si la
    // session est terminée, on ne doit pas déconnecter l'utilisateur pour ça.
    TransientFailure
}

public sealed record TokenRefreshResult(TokenRefreshOutcome Outcome, string? AccessToken);

// Donne un access_token valide pour une session, en le renouvelant au
// besoin. Un verrou par session évite que deux requêtes concurrentes
// déclenchent deux renouvellements : OpenIddict fait tourner les
// refresh_token (chaque renouvellement révoque le précédent), donc un
// double renouvellement ferait échouer le second appel.
public sealed class TokenRefreshService
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly TokenStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<TokenRefreshService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public TokenRefreshService(
        TokenStore store,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<TokenRefreshService> logger)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<TokenRefreshResult> GetValidAccessTokenAsync(
        string sessionId, CancellationToken ct = default)
    {
        var stored = _store.Get(sessionId);
        if (stored is null)
            return new TokenRefreshResult(TokenRefreshOutcome.ReauthRequired, null);

        if (stored.ExpiresAtUtc - DateTimeOffset.UtcNow > RefreshMargin)
            return new TokenRefreshResult(TokenRefreshOutcome.Valid, stored.AccessToken);

        var sem = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // Un autre appel concurrent a peut-être déjà renouvelé pendant
            // qu'on attendait le verrou : on revérifie avant d'agir.
            stored = _store.Get(sessionId);
            if (stored is null)
                return new TokenRefreshResult(TokenRefreshOutcome.ReauthRequired, null);

            if (stored.ExpiresAtUtc - DateTimeOffset.UtcNow > RefreshMargin)
                return new TokenRefreshResult(TokenRefreshOutcome.Valid, stored.AccessToken);

            if (string.IsNullOrEmpty(stored.RefreshToken))
            {
                _logger.LogWarning(
                    "Pas de refresh_token disponible pour la session : ré-authentification requise.");
                _store.Remove(sessionId);
                return new TokenRefreshResult(TokenRefreshOutcome.ReauthRequired, null);
            }

            HttpResponseMessage response;
            try
            {
                var client = _httpClientFactory.CreateClient();
                response = await client.PostAsync(
                    $"{_config["Sso:Authority"]}/connect/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = stored.RefreshToken,
                        ["client_id"] = _config["Sso:ClientId"]!,
                        ["client_secret"] = _config["Sso:ClientSecret"]!
                    }), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Échec réseau lors du renouvellement du jeton.");
                return new TokenRefreshResult(TokenRefreshOutcome.TransientFailure, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                // OpenIddict renvoie 400 invalid_grant si le refresh_token est
                // invalide, révoqué ou expiré : fin légitime de session.
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Renouvellement refusé par SsoServer ({Status}) : {Body}", response.StatusCode, body);
                _store.Remove(sessionId);
                return new TokenRefreshResult(TokenRefreshOutcome.ReauthRequired, null);
            }

            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
            if (tokens is null)
            {
                _logger.LogWarning("Réponse de renouvellement vide ou invalide.");
                return new TokenRefreshResult(TokenRefreshOutcome.TransientFailure, null);
            }

            var refreshed = new StoredTokens
            {
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken ?? stored.RefreshToken,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn)
            };
            _store.Set(sessionId, refreshed);

            return new TokenRefreshResult(TokenRefreshOutcome.Valid, refreshed.AccessToken);
        }
        finally
        {
            sem.Release();
        }
    }
}

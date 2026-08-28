using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;

namespace ClientApi.Services;

public sealed class StoredTokens
{
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}

// Garde les jetons OAuth côté serveur ; le cookie de session ne porte
// qu'une clé opaque vers cette entrée, jamais les jetons eux-mêmes.
public sealed class TokenStore
{
    private readonly IMemoryCache _cache;

    // Aligné sur ConfigureApplicationCookie/Program.cs : un jeton ne doit
    // pas survivre plus longtemps que le cookie de session qui y renvoie.
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(8);

    public TokenStore(IMemoryCache cache) => _cache = cache;

    public static string NewSessionId() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
               .Replace("+", "-").Replace("/", "_").TrimEnd('=');

    public void Set(string sessionId, StoredTokens tokens) =>
        _cache.Set(Key(sessionId), tokens, MaxLifetime);

    public StoredTokens? Get(string sessionId) =>
        _cache.Get<StoredTokens>(Key(sessionId));

    public void Remove(string sessionId) =>
        _cache.Remove(Key(sessionId));

    private static string Key(string sessionId) => $"tokens:{sessionId}";
}

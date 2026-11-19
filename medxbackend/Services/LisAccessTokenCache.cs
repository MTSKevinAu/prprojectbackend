using medxbackend.Models;

namespace medxbackend.Services;

public sealed class LisAccessTokenCache
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly object stateLock = new();
    private LisTokenResponse? cachedToken;
    private DateTimeOffset usableUntil;

    public async Task<LisTokenResponse?> GetOrCreateAsync(
        Func<CancellationToken, Task<LisTokenResponse?>> acquireToken,
        CancellationToken cancellationToken)
    {
        var token = GetUsableToken();
        if (token is not null)
        {
            return token;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            token = GetUsableToken();
            if (token is not null)
            {
                return token;
            }

            token = await acquireToken(cancellationToken);
            if (token is not null)
            {
                var lifetimeSeconds = token.ExpiresIn;
                var safetySeconds = Math.Min(30d, Math.Max(1d, lifetimeSeconds * 0.1d));
                var cacheSeconds = lifetimeSeconds - safetySeconds;
                if (cacheSeconds > 0)
                {
                    lock (stateLock)
                    {
                        cachedToken = token;
                        usableUntil = DateTimeOffset.UtcNow.AddSeconds(cacheSeconds);
                    }
                }
            }

            return token;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public void Invalidate(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        lock (stateLock)
        {
            if (string.Equals(cachedToken?.AccessToken, accessToken, StringComparison.Ordinal))
            {
                cachedToken = null;
                usableUntil = DateTimeOffset.MinValue;
            }
        }
    }

    private LisTokenResponse? GetUsableToken()
    {
        lock (stateLock)
        {
            return DateTimeOffset.UtcNow < usableUntil ? cachedToken : null;
        }
    }
}
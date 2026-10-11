using Microsoft.Extensions.Caching.Memory;

namespace AppInsightsLogs.Api.Services;

/// <summary>Caches query results for a few seconds. Failures are never cached.</summary>
public sealed class QueryCache(IMemoryCache cache)
{
    public async Task<T> GetOrAddAsync<T>(string key, int seconds, bool bypass, Func<Task<T>> factory)
    {
        if (seconds <= 0)
        {
            return await factory();
        }
        if (!bypass && cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }
        var value = await factory();
        cache.Set(key, value, TimeSpan.FromSeconds(seconds));
        return value;
    }
}

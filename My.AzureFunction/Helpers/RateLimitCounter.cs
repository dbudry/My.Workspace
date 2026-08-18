using Microsoft.Extensions.Caching.Memory;
using My.Shared.Rules;

namespace My.Functions.Helpers;

/// <summary>
/// Fixed one-minute window counter backed by <see cref="IMemoryCache"/>.
/// </summary>
internal static class RateLimitCounter
{
    public static bool TryAcquire(
        IMemoryCache cache,
        RateLimitPolicy policy,
        DateTimeOffset utcNow,
        out int retryAfterSeconds)
    {
        retryAfterSeconds = RateLimitRules.ComputeRetryAfterSeconds(utcNow);
        if (policy.IsExempt)
            return true;

        var windowId = utcNow.ToUnixTimeSeconds() / 60;
        var cacheKey = $"ratelimit:{policy.BucketKey}:{windowId}";

        var count = cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            return 0;
        });

        count++;
        cache.Set(cacheKey, count, TimeSpan.FromMinutes(2));

        if (count > policy.PermitsPerMinute)
            return false;

        return true;
    }
}
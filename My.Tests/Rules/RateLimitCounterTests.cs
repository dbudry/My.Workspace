using Microsoft.Extensions.Caching.Memory;
using My.Functions.Helpers;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class RateLimitCounterTests
{
    [Fact]
    public void TryAcquire_allows_up_to_permit_count_then_blocks()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var policy = new RateLimitPolicy("test-bucket", 2);
        var utcNow = DateTimeOffset.UtcNow;

        Assert.True(RateLimitCounter.TryAcquire(cache, policy, utcNow, out var retry1));
        Assert.True(retry1 > 0);
        Assert.True(RateLimitCounter.TryAcquire(cache, policy, utcNow, out _));
        Assert.False(RateLimitCounter.TryAcquire(cache, policy, utcNow, out var retry3));
        Assert.True(retry3 > 0);
    }

    [Fact]
    public void TryAcquire_exempt_policy_always_allows()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var utcNow = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
            Assert.True(RateLimitCounter.TryAcquire(cache, RateLimitPolicy.Exempt, utcNow, out _));
    }
}
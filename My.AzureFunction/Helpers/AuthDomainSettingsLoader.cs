using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using My.DAL.Data;
using My.Shared.Rules;

namespace My.Functions.Helpers;

/// <summary>
/// Loads allowed email domain policy from AppSettings (setup wizard / admin),
/// with short cache. Environment variable <see cref="GoogleIdentityRules.AllowedEmailDomainsEnvKey"/>
/// still wins via <see cref="GoogleIdentityRules.ResolveConfiguredDomains"/>.
/// </summary>
internal static class AuthDomainSettingsLoader
{
    private const string CacheKey = "auth:allowed-email-domains";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public static async Task<string?> LoadDatabaseValueAsync(
        ApplicationDbContext dbContext,
        IMemoryCache cache)
    {
        if (cache.TryGetValue(CacheKey, out string? cached))
            return cached;

        var value = await dbContext.AppSettings.AsNoTracking()
            .Where(s => s.Key == GoogleIdentityRules.AllowedEmailDomainsSettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync();

        cache.Set(CacheKey, value, CacheDuration);
        return value;
    }

    public static async Task<string> ResolveAsync(
        ApplicationDbContext dbContext,
        IMemoryCache cache)
    {
        var dbValue = await LoadDatabaseValueAsync(dbContext, cache);
        return GoogleIdentityRules.ResolveConfiguredDomains(dbValue);
    }

    public static void Invalidate(IMemoryCache cache) => cache.Remove(CacheKey);
}

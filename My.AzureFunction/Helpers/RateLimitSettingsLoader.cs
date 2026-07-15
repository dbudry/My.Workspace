using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using My.DAL.Data;
using My.Shared.Rules;
using ConstantsClass = My.Shared.Constants.Constants;

namespace My.Functions.Helpers;

internal static class RateLimitSettingsLoader
{
    private const string CacheKey = "ratelimit:settings";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private static readonly string[] SettingKeys =
    [
        ConstantsClass.SettingKeys.RateLimitEnabled,
        ConstantsClass.SettingKeys.RateLimitAuthenticatedPerMinute,
        ConstantsClass.SettingKeys.RateLimitAnonymousPerMinute,
        ConstantsClass.SettingKeys.RateLimitInvalidBearerPerMinute,
        ConstantsClass.SettingKeys.RateLimitProvisionPerMinute,
        ConstantsClass.SettingKeys.RateLimitFetchExternalImagePerMinute,
        ConstantsClass.SettingKeys.RateLimitUploadPerMinute,
        ConstantsClass.SettingKeys.RateLimitHeavyReadPerMinute
    ];

    public static async Task<RateLimitSettings> LoadAsync(
        ApplicationDbContext dbContext,
        IMemoryCache cache,
        string? configurationEnabledRaw)
    {
        if (cache.TryGetValue(CacheKey, out RateLimitSettings? cached) && cached != null)
            return cached;

        var rows = await dbContext.AppSettings.AsNoTracking()
            .Where(s => SettingKeys.Contains(s.Key))
            .Select(s => new { s.Key, s.Value })
            .ToListAsync();

        var settings = RateLimitSettings.ParseFromAppSettingsRows(
            rows.Select(r => (r.Key, (string?)r.Value)),
            configurationEnabledRaw);

        cache.Set(CacheKey, settings, CacheDuration);
        return settings;
    }

    public static void Invalidate(IMemoryCache cache) => cache.Remove(CacheKey);
}

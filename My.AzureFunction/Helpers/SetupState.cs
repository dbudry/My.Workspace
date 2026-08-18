using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using My.DAL.Data;
using My.DAL.Models;
using My.Shared.Constants;
using My.Shared.Rules;

namespace My.Functions.Helpers;

/// <summary>First-run setup flags stored in AppSettings.</summary>
internal static class SetupState
{
    private const string StatusCacheKey = "setup:status-snapshot";
    private static readonly TimeSpan StatusCacheDuration = TimeSpan.FromSeconds(15);

    public static async Task<bool> IsCompletedAsync(ApplicationDbContext db, IMemoryCache cache)
    {
        var usersExist = await db.ApplicationUsers.AsNoTracking().AnyAsync();
        if (usersExist)
            return true;

        var flag = await GetSettingAsync(db, cache, Constants.SettingKeys.SetupCompleted);
        return string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task MarkCompletedAsync(ApplicationDbContext db, IMemoryCache cache)
    {
        await UpsertSettingAsync(db,
            Constants.SettingKeys.SetupCompleted,
            "true",
            "When true, the first-run setup wizard is complete.");
        Invalidate(cache);
    }

    public static async Task UpsertSettingAsync(
        ApplicationDbContext db,
        string key,
        string value,
        string description)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = value,
                Description = description
            });
        }
        else
        {
            row.Value = value;
            if (!string.IsNullOrWhiteSpace(description))
                row.Description = description;
        }

        await db.SaveChangesAsync();
    }

    public static async Task<string?> GetSettingAsync(ApplicationDbContext db, IMemoryCache cache, string key)
    {
        // Small per-key cache via composite; status endpoint uses its own snapshot cache.
        return await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
    }

    public static void Invalidate(IMemoryCache cache)
    {
        cache.Remove(StatusCacheKey);
        AuthDomainSettingsLoader.Invalidate(cache);
    }

    public static string StatusCacheKeyName => StatusCacheKey;
    public static TimeSpan StatusCacheTtl => StatusCacheDuration;
}

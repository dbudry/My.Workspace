using ConstantsClass = My.Shared.Constants.Constants;

namespace My.Shared.Rules;

/// <summary>
/// Workspace rate-limit configuration: on/off plus per-bucket permits per minute.
/// Values come from App Settings (preferred) or environment defaults.
/// </summary>
public sealed class RateLimitSettings
{
    public bool Enabled { get; init; }

    public int AuthenticatedPerMinute { get; init; } = RateLimitRules.DefaultAuthenticatedPerMinute;
    public int AnonymousPerMinute { get; init; } = RateLimitRules.DefaultAnonymousPerMinute;
    public int InvalidBearerPerMinute { get; init; } = RateLimitRules.InvalidBearerPerMinute;
    public int ProvisionPerMinute { get; init; } = RateLimitRules.ProvisionPerMinute;
    public int FetchExternalImagePerMinute { get; init; } = RateLimitRules.FetchExternalImagePerMinute;
    public int UploadPerMinute { get; init; } = RateLimitRules.UploadPerMinute;
    public int HeavyReadPerMinute { get; init; } = RateLimitRules.HeavyReadPerMinute;

    public static RateLimitSettings Disabled { get; } = new() { Enabled = false };

    public const int MinPermitsPerMinute = 1;
    public const int MaxPermitsPerMinute = 10_000;

    /// <summary>
    /// Resolves full settings. Database App Settings win when set for each key;
    /// missing limit keys fall back to <see cref="RateLimitRules"/> defaults.
    /// Enabled defaults to <c>false</c> so admins opt in from App Settings.
    /// </summary>
    public static RateLimitSettings Resolve(
        string? appSettingEnabledRaw,
        string? configurationEnabledRaw,
        IReadOnlyDictionary<string, string?>? limitValues = null)
    {
        var enabled = false;
        if (TryParseEnabled(appSettingEnabledRaw, out var fromDb))
            enabled = fromDb;
        else if (TryParseEnabled(configurationEnabledRaw, out var fromConfig))
            enabled = fromConfig;

        return new RateLimitSettings
        {
            Enabled = enabled,
            AuthenticatedPerMinute = ReadLimit(limitValues, ConstantsClass.SettingKeys.RateLimitAuthenticatedPerMinute, RateLimitRules.DefaultAuthenticatedPerMinute),
            AnonymousPerMinute = ReadLimit(limitValues, ConstantsClass.SettingKeys.RateLimitAnonymousPerMinute, RateLimitRules.DefaultAnonymousPerMinute),
            InvalidBearerPerMinute = ReadLimit(limitValues, ConstantsClass.SettingKeys.RateLimitInvalidBearerPerMinute, RateLimitRules.InvalidBearerPerMinute),
            ProvisionPerMinute = ReadLimit(limitValues, ConstantsClass.SettingKeys.RateLimitProvisionPerMinute, RateLimitRules.ProvisionPerMinute),
            FetchExternalImagePerMinute = ReadLimit(limitValues, ConstantsClass.SettingKeys.RateLimitFetchExternalImagePerMinute, RateLimitRules.FetchExternalImagePerMinute),
            UploadPerMinute = ReadLimit(limitValues, ConstantsClass.SettingKeys.RateLimitUploadPerMinute, RateLimitRules.UploadPerMinute),
            HeavyReadPerMinute = ReadLimit(limitValues, ConstantsClass.SettingKeys.RateLimitHeavyReadPerMinute, RateLimitRules.HeavyReadPerMinute)
        };
    }

    /// <summary>Backward-compatible overload used by older tests.</summary>
    public static RateLimitSettings Resolve(string? appSettingEnabledRaw, string? configurationEnabledRaw) =>
        Resolve(appSettingEnabledRaw, configurationEnabledRaw, limitValues: null);

    public static RateLimitSettings ParseFromAppSettingsRows(
        IEnumerable<(string Key, string? Value)>? settings,
        string? configurationEnabledRaw = null)
    {
        string? enabledRaw = null;
        var limits = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (settings != null)
        {
            foreach (var (key, value) in settings)
            {
                if (string.Equals(key, ConstantsClass.SettingKeys.RateLimitEnabled, StringComparison.Ordinal))
                    enabledRaw = value;
                else if (IsRateLimitLimitKey(key))
                    limits[key] = value;
            }
        }

        return Resolve(enabledRaw, configurationEnabledRaw, limits);
    }

    public RateLimitOptions ToOptions() =>
        RateLimitRules.BuildOptions(
            Enabled,
            AuthenticatedPerMinute,
            AnonymousPerMinute,
            InvalidBearerPerMinute,
            ProvisionPerMinute,
            FetchExternalImagePerMinute,
            UploadPerMinute,
            HeavyReadPerMinute);

    public static int ClampPermits(int value) =>
        Math.Clamp(value, MinPermitsPerMinute, MaxPermitsPerMinute);

    public static bool IsRateLimitLimitKey(string key) =>
        key is ConstantsClass.SettingKeys.RateLimitAuthenticatedPerMinute
            or ConstantsClass.SettingKeys.RateLimitAnonymousPerMinute
            or ConstantsClass.SettingKeys.RateLimitInvalidBearerPerMinute
            or ConstantsClass.SettingKeys.RateLimitProvisionPerMinute
            or ConstantsClass.SettingKeys.RateLimitFetchExternalImagePerMinute
            or ConstantsClass.SettingKeys.RateLimitUploadPerMinute
            or ConstantsClass.SettingKeys.RateLimitHeavyReadPerMinute;

    private static int ReadLimit(IReadOnlyDictionary<string, string?>? limits, string key, int fallback)
    {
        if (limits == null || !limits.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return fallback;

        return int.TryParse(raw.Trim(), out var parsed)
            ? ClampPermits(parsed)
            : fallback;
    }

    private static bool TryParseEnabled(string? raw, out bool enabled)
    {
        enabled = false;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return bool.TryParse(raw.Trim(), out enabled);
    }
}

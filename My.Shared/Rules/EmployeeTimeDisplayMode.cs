using ConstantsClass = My.Shared.Constants.Constants;

namespace My.Shared.Rules;

/// <summary>
/// How Tasks / Calendar / Reports show original employee time vs manager adjustments.
/// Controlled only by App Settings (workspace-wide); pages do not offer a user toggle.
/// Default is <see cref="Both"/> so existing dual-row behavior is preserved when unset.
/// </summary>
public enum EmployeeTimeDisplayMode
{
    /// <summary>Only the employee's original submitted values.</summary>
    TheirTime = 0,

    /// <summary>Only manager-adjusted values when a correction exists; otherwise the original.</summary>
    Adjusted = 1,

    /// <summary>Original plus overlay row/chip when a manager correction exists.</summary>
    Both = 2
}

public static class EmployeeTimeDisplayModeRules
{
    public const string TheirTimeKey = "their";
    public const string AdjustedKey = "adjusted";
    public const string BothKey = "both";

    public static EmployeeTimeDisplayMode Parse(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            TheirTimeKey or "theirtime" or "original" => EmployeeTimeDisplayMode.TheirTime,
            AdjustedKey or "adjustedtime" or "manager" => EmployeeTimeDisplayMode.Adjusted,
            _ => EmployeeTimeDisplayMode.Both
        };

    public static string ToStorageKey(EmployeeTimeDisplayMode mode) => mode switch
    {
        EmployeeTimeDisplayMode.TheirTime => TheirTimeKey,
        EmployeeTimeDisplayMode.Adjusted => AdjustedKey,
        _ => BothKey
    };

    /// <summary>
    /// Workspace mode from App Settings. Falls back to <see cref="Both"/> when missing.
    /// </summary>
    public static EmployeeTimeDisplayMode FromAppSettings(IReadOnlyDictionary<string, string>? settingsByKey)
    {
        if (settingsByKey == null)
            return EmployeeTimeDisplayMode.Both;
        settingsByKey.TryGetValue(ConstantsClass.SettingKeys.TymeEmployeeTimeDisplayMode, out var raw);
        return Parse(raw);
    }

    public static EmployeeTimeDisplayMode FromAppSettings(IEnumerable<KeyValuePair<string, string?>> settings)
    {
        foreach (var s in settings)
        {
            if (string.Equals(s.Key, ConstantsClass.SettingKeys.TymeEmployeeTimeDisplayMode, StringComparison.OrdinalIgnoreCase))
                return Parse(s.Value);
        }
        return EmployeeTimeDisplayMode.Both;
    }

    /// <summary>
    /// Whether to include the original (employee) row for a task that may have a correction.
    /// </summary>
    public static bool IncludeOriginal(EmployeeTimeDisplayMode mode, bool hasAdjustment) =>
        mode switch
        {
            EmployeeTimeDisplayMode.TheirTime => true,
            EmployeeTimeDisplayMode.Adjusted => !hasAdjustment,
            _ => true
        };

    /// <summary>
    /// Whether to include the manager adjustment overlay for a corrected task.
    /// </summary>
    public static bool IncludeAdjustmentOverlay(EmployeeTimeDisplayMode mode, bool hasAdjustment) =>
        hasAdjustment && mode is EmployeeTimeDisplayMode.Adjusted or EmployeeTimeDisplayMode.Both;
}

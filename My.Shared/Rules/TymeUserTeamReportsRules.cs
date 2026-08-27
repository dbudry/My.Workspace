using ConstantsClass = My.Shared.Constants.Constants;

namespace My.Shared.Rules;

/// <summary>
/// Workspace opt-in so Tyme users (not only Manager:Tyme) can view other people's
/// Reports. Default when the setting is missing: off.
/// </summary>
public static class TymeUserTeamReportsRules
{
    public const bool DefaultEnabled = false;

    public static bool IsEnabled(string? settingValue)
    {
        if (string.IsNullOrWhiteSpace(settingValue))
            return DefaultEnabled;

        return bool.TryParse(settingValue, out var parsed) && parsed;
    }

    public static bool IsEnabled(IReadOnlyDictionary<string, string> settingsByKey)
    {
        settingsByKey.TryGetValue(ConstantsClass.SettingKeys.TymeAllowUserTeamReports, out var value);
        return IsEnabled(value);
    }

    public static string DisabledMessage =>
        "Viewing other people's Reports is disabled in App Settings (Tyme).";
}

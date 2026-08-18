using ConstantsClass = My.Shared.Constants.Constants;

namespace My.Shared.Rules;

/// <summary>
/// Gates manager/admin submission of an employee's month when they cannot submit themselves.
/// Workspace opt-in via App Settings (<see cref="ConstantsClass.SettingKeys.TymeAllowManagerSubmitOnBehalf"/>).
/// Default when the setting is missing: off (must be enabled deliberately).
/// </summary>
public static class ManagerSubmitOnBehalfRules
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
        settingsByKey.TryGetValue(ConstantsClass.SettingKeys.TymeAllowManagerSubmitOnBehalf, out var value);
        return IsEnabled(value);
    }

    public static string DisabledMessage =>
        "Manager submit on behalf of employees is disabled in App Settings (Tyme).";
}

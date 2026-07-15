using ConstantsClass = My.Shared.Constants.Constants;

namespace My.Shared.Rules;

/// <summary>
/// Gates manager post-submission time corrections based on App Settings.
/// The workspace uses exactly one correction mode (alias or direct), not both.
/// When settings are turned off, existing corrections remain; only new
/// creates and edits are blocked. Delete/revert stays allowed.
/// </summary>
public static class ManagerCorrectionRules
{
    public enum CorrectionMode
    {
        Alias,
        Direct,
    }

    public enum CorrectionAction
    {
        Create,
        Update,
        Delete,
    }

    public static ManagerCorrectionSettings Parse(
        string? allowManagerCorrection,
        string? correctionMode)
    {
        return new ManagerCorrectionSettings(
            ParseBool(allowManagerCorrection, defaultValue: true),
            ParseMode(correctionMode));
    }

    public static ManagerCorrectionSettings Parse(IReadOnlyDictionary<string, string> settingsByKey)
    {
        settingsByKey.TryGetValue(ConstantsClass.SettingKeys.TymeAllowManagerTimeCorrection, out var master);
        settingsByKey.TryGetValue(ConstantsClass.SettingKeys.TymeManagerCorrectionMode, out var mode);
        return Parse(master, mode);
    }

    public static Decision ValidateSettings(bool allowManagerCorrection, CorrectionMode mode)
    {
        if (!allowManagerCorrection)
            return Decision.Allowed();

        if (Enum.IsDefined(mode))
            return Decision.Allowed();

        return Decision.Blocked("Manager correction mode must be Alias or Direct.");
    }

    public static Decision Evaluate(CorrectionMode mode, CorrectionAction action, ManagerCorrectionSettings settings)
    {
        if (action == CorrectionAction.Delete)
            return Decision.Allowed();

        if (!settings.AllowManagerCorrection)
        {
            return action == CorrectionAction.Create
                ? Decision.Blocked("Manager time corrections are disabled in App Settings.")
                : Decision.Blocked("Editing manager corrections is disabled in App Settings.");
        }

        if (mode != settings.Mode)
        {
            return Decision.Blocked(
                settings.Mode == CorrectionMode.Alias
                    ? "This workspace uses alias corrections only. Direct edits are not allowed."
                    : "This workspace uses direct corrections only. Alias overlays are not allowed.");
        }

        return Decision.Allowed();
    }

    public static CorrectionMode ParseMode(string? value)
    {
        if (string.Equals(value, nameof(CorrectionMode.Direct), StringComparison.OrdinalIgnoreCase))
            return CorrectionMode.Direct;

        return CorrectionMode.Alias;
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    public sealed record Decision(bool IsAllowed, string? Reason)
    {
        public static Decision Allowed() => new(true, null);
        public static Decision Blocked(string reason) => new(false, reason);
    }
}
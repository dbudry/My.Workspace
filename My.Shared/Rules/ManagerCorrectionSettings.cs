namespace My.Shared.Rules;

/// <summary>
/// Workspace-wide manager time-correction flags from App Settings.
/// When correction is enabled, exactly one mode applies: alias or direct — never both.
/// </summary>
public sealed record ManagerCorrectionSettings(
    bool AllowManagerCorrection,
    ManagerCorrectionRules.CorrectionMode Mode)
{
    public static ManagerCorrectionSettings Defaults { get; } =
        new(true, ManagerCorrectionRules.CorrectionMode.Alias);

    public bool CanCreateOrUpdateAlias =>
        AllowManagerCorrection && Mode == ManagerCorrectionRules.CorrectionMode.Alias;

    public bool CanCreateOrUpdateDirect =>
        AllowManagerCorrection && Mode == ManagerCorrectionRules.CorrectionMode.Direct;
}
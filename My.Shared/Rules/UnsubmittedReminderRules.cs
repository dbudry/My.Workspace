using My.Shared.Dtos.TimeSubmission;
using ConstantsClass = My.Shared.Constants.Constants;

namespace My.Shared.Rules;

/// <summary>
/// Rules for the "remind an employee about unsubmitted time" manager action.
/// Workspace opt-in via App Settings (<see cref="ConstantsClass.SettingKeys.TymeUnsubmittedReminderEnabled"/>).
/// Default when the setting is missing: off.
/// </summary>
public static class UnsubmittedReminderRules
{
    public const bool DefaultEnabled = false;

    public const string CoverageMonth = "month";
    public const string CoverageRange = "range";
    public const string CoverageYear = "year";

    public const string UnsubmittedKey = "unsubmitted";

    /// <summary>Coverage choices in display order, with their labels.</summary>
    public static readonly IReadOnlyList<(string Key, string Text)> CoverageOptions =
    [
        (CoverageMonth, "By month clicked"),
        (CoverageRange, "By month range"),
        (CoverageYear, "By year")
    ];

    public static bool IsCoverage(string? value) =>
        CoverageOptions.Any(o => o.Key == value);

    public static string ParseCoverage(string? value) =>
        IsCoverage(value) ? value! : CoverageMonth;

    public static bool IsEnabled(string? settingValue)
    {
        if (string.IsNullOrWhiteSpace(settingValue))
            return DefaultEnabled;
        return bool.TryParse(settingValue, out var parsed) && parsed;
    }

    public static string DisabledMessage =>
        "Reminder emails are turned off in App Settings (Tyme).";

    /// <summary>True when the cell is a past month with time that is not submitted.</summary>
    public static bool IsUnsubmitted(SubmissionGridCellDto cell) =>
        cell.Status == SubmissionCellStatus.Unsubmitted;

    /// <summary>
    /// Months the reminder grid looks at, counted from the clicked month: just that month, the months
    /// before and after it, or its calendar year.
    /// </summary>
    public static ((int Year, int Month) From, (int Year, int Month) To) Window(
        string coverage, int year, int month, int monthsBefore, int monthsAfter) =>
        ParseCoverage(coverage) switch
        {
            CoverageRange => (
                SubmissionGridRules.AddMonths(year, month, -Math.Clamp(monthsBefore, 0, SubmissionGridRules.MaxOffset)),
                SubmissionGridRules.AddMonths(year, month, Math.Clamp(monthsAfter, 0, SubmissionGridRules.MaxOffset))),
            CoverageYear => ((year, 1), (year, 12)),
            _ => ((year, month), (year, month))
        };

    /// <summary>
    /// Statuses the reminder grid may show. Unsubmitted is always on. By month clicked it is the only one,
    /// so every other box is cleared. Unknown keys are dropped. Order follows the status list.
    /// </summary>
    public static List<string> NormalizeStatuses(string coverage, IEnumerable<string>? picked)
    {
        if (ParseCoverage(coverage) == CoverageMonth)
            return [UnsubmittedKey];

        var set = (picked ?? []).ToHashSet(StringComparer.Ordinal);
        set.Add(UnsubmittedKey);
        return SubmissionGridRules.StatusOptions.Select(o => o.Key).Where(set.Contains).ToList();
    }

    /// <summary>Stored form: comma list.</summary>
    public static string StatusesToText(IEnumerable<string> statuses) => string.Join(",", statuses);

    public static List<string> ParseStatuses(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>"Aug 2026", "Aug 2026 and Sep 2026", "Aug 2026, Sep 2026 and Oct 2026".</summary>
    public static string MonthList(IReadOnlyList<SubmissionGridMonthDto> months)
    {
        var labels = months.Select(m => SubmissionGridRules.MonthLabel(m.Year, m.Month)).ToList();
        return labels.Count switch
        {
            0 => "",
            1 => labels[0],
            _ => string.Join(", ", labels.Take(labels.Count - 1)) + " and " + labels[^1]
        };
    }
}

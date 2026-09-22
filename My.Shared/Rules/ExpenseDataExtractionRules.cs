namespace My.Shared.Rules;

/// <summary>
/// Manager:Expenses extract. Closed categories stay on the line sheet so a later
/// QuickBooks account map can key off the same values without remodeling.
/// </summary>
public static class ExpenseDataExtractionRules
{
    public const string Reports = "Reports";
    public const string Lines = "Lines";
    public const string Receipts = "Receipts";

    public const string StatusAll = "all";
    public const string StatusDraft = "draft";
    public const string StatusSubmitted = "submitted";
    public const string StatusReimbursed = "reimbursed";

    public static readonly IReadOnlyList<string> AllEntities =
    [
        Reports,
        Lines,
        Receipts
    ];

    public static readonly IReadOnlyList<string> AllStatuses =
    [
        StatusAll,
        StatusDraft,
        StatusSubmitted,
        StatusReimbursed
    ];

    public static bool TryParseEntities(string? raw, out List<string> entities, out string? error)
    {
        entities = [];
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Select at least one dataset.";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = AllEntities.FirstOrDefault(e => string.Equals(e, part, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                error = $"Unknown dataset '{part}'.";
                entities = [];
                return false;
            }

            if (seen.Add(match))
                entities.Add(match);
        }

        if (entities.Count == 0)
        {
            error = "Select at least one dataset.";
            return false;
        }

        return true;
    }

    public static bool TryParseStatus(
        string? raw,
        out string status,
        out string? error,
        string defaultStatus = StatusSubmitted)
    {
        status = defaultStatus;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var match = AllStatuses.FirstOrDefault(s => string.Equals(s, raw.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            error = "Status must be all, draft, submitted, or reimbursed.";
            status = StatusAll;
            return false;
        }

        status = match;
        return true;
    }

    public static string? ValidateRequest(IReadOnlyCollection<string> entities, int? year, int? month)
    {
        if (entities is null || entities.Count == 0)
            return "Select at least one dataset.";
        if (entities.Any(e => !AllEntities.Contains(e, StringComparer.OrdinalIgnoreCase)))
            return "Unknown dataset.";
        if (year is < 2000 or > 9999)
            return "Invalid year.";
        if (month is < 1 or > 12)
            return "Invalid month.";
        if (month.HasValue && !year.HasValue)
            return "Month requires a year.";
        return null;
    }

    public static bool MatchesStatus(string? reportStatus, string filter)
    {
        if (string.Equals(filter, StatusAll, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(filter, StatusDraft, StringComparison.OrdinalIgnoreCase))
            return ExpenseStatusRules.IsDraft(reportStatus);
        if (string.Equals(filter, StatusSubmitted, StringComparison.OrdinalIgnoreCase))
            return ExpenseStatusRules.IsSubmitted(reportStatus);
        if (string.Equals(filter, StatusReimbursed, StringComparison.OrdinalIgnoreCase))
            return ExpenseStatusRules.IsReimbursed(reportStatus);
        return false;
    }

    /// <summary>Stored Status value for a filter, or null when the filter is all.</summary>
    public static string? StoredStatusForFilter(string filter)
    {
        if (string.Equals(filter, StatusDraft, StringComparison.OrdinalIgnoreCase))
            return ExpenseStatusRules.Draft;
        if (string.Equals(filter, StatusSubmitted, StringComparison.OrdinalIgnoreCase))
            return ExpenseStatusRules.Submitted;
        if (string.Equals(filter, StatusReimbursed, StringComparison.OrdinalIgnoreCase))
            return ExpenseStatusRules.Reimbursed;
        return null;
    }
}

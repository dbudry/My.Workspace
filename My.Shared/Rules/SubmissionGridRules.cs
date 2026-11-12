using System.Globalization;
using System.Text.Json;
using My.Shared.Dtos.TimeSubmission;

namespace My.Shared.Rules;

/// <summary>
/// Pure helpers for the Tyme submission grid (Submit page Grid view and the submit email):
/// default month window, range validation, and cell status.
/// </summary>
public static class SubmissionGridRules
{
    /// <summary>Most months one grid may span.</summary>
    public const int MaxMonths = 12;

    public const int DefaultMonthsBefore = 1;
    public const int DefaultMonthsAfter = 1;

    /// <summary>Settings offsets are clamped to this many months either side.</summary>
    public const int MaxOffset = 11;

    public static (int Year, int Month) AddMonths(int year, int month, int delta)
    {
        var d = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(delta);
        return (d.Year, d.Month);
    }

    /// <summary>Window of <paramref name="before"/> months before to <paramref name="after"/> months after the current month.</summary>
    public static ((int Year, int Month) From, (int Year, int Month) To) RangeFromOffsets(
        DateTime utcNow, int before, int after)
    {
        before = Math.Clamp(before, 0, MaxOffset);
        after = Math.Clamp(after, 0, MaxOffset);
        return (AddMonths(utcNow.Year, utcNow.Month, -before), AddMonths(utcNow.Year, utcNow.Month, after));
    }

    /// <summary>Last month, this month, next month.</summary>
    public static ((int Year, int Month) From, (int Year, int Month) To) DefaultRange(DateTime utcNow) =>
        RangeFromOffsets(utcNow, DefaultMonthsBefore, DefaultMonthsAfter);

    /// <summary>Number of months in the inclusive range, or 0 when To is before From.</summary>
    public static int CountMonths((int Year, int Month) from, (int Year, int Month) to)
    {
        var n = (to.Year - from.Year) * 12 + (to.Month - from.Month) + 1;
        return n < 1 ? 0 : n;
    }

    public static bool IsValidRange((int Year, int Month) from, (int Year, int Month) to)
    {
        if (from.Month is < 1 or > 12 || to.Month is < 1 or > 12) return false;
        if (from.Year is < 2000 or > 9999 || to.Year is < 2000 or > 9999) return false;
        var n = CountMonths(from, to);
        return n >= 1 && n <= MaxMonths;
    }

    /// <summary>
    /// True when the month is inside the inclusive range. Either end may be null for open-ended.
    /// Used by the Team list, which has no 12-month cap.
    /// </summary>
    public static bool IsInRange(int year, int month, (int Year, int Month)? from, (int Year, int Month)? to)
    {
        var index = year * 12 + month;
        if (from.HasValue && index < from.Value.Year * 12 + from.Value.Month) return false;
        if (to.HasValue && index > to.Value.Year * 12 + to.Value.Month) return false;
        return true;
    }

    public static List<(int Year, int Month)> EnumerateMonths((int Year, int Month) from, (int Year, int Month) to)
    {
        var list = new List<(int, int)>();
        var n = CountMonths(from, to);
        for (var i = 0; i < n && i < MaxMonths; i++)
            list.Add(AddMonths(from.Year, from.Month, i));
        return list;
    }

    /// <summary>Parses "yyyy-MM". Null when the text is not a valid month.</summary>
    public static (int Year, int Month)? ParseMonth(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!DateTime.TryParseExact(text.Trim(), "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d))
            return null;
        return (d.Year, d.Month);
    }

    public static string FormatMonth(int year, int month) =>
        new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static string MonthLabel(int year, int month) =>
        new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).ToString("MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// Submitted wins. Otherwise a month with no tracked time is NoData; a past month with
    /// time is Unsubmitted; the current or a future month with time is Open.
    /// </summary>
    public static SubmissionCellStatus ResolveStatus(
        bool submitted, bool hasTrackedTime, int year, int month, DateTime utcNow)
    {
        if (submitted) return SubmissionCellStatus.Submitted;
        if (!hasTrackedTime) return SubmissionCellStatus.NoData;
        return TimeSubmissionRules.IsEarlySubmission(year, month, utcNow)
            ? SubmissionCellStatus.Open
            : SubmissionCellStatus.Unsubmitted;
    }

    /// <summary>Status keys used by the Status filter on Grid and Team, in display order.</summary>
    public static readonly IReadOnlyList<(string Key, string Text)> StatusOptions =
    [
        ("submitted", "Submitted"),
        ("unsubmitted", "Not submitted"),
        ("open", "In progress"),
        ("notime", "No time")
    ];

    public static string StatusKey(SubmissionCellStatus status) => status switch
    {
        SubmissionCellStatus.Submitted => "submitted",
        SubmissionCellStatus.Unsubmitted => "unsubmitted",
        SubmissionCellStatus.Open => "open",
        _ => "notime"
    };

    /// <summary>
    /// Grid status filter, per cell. A null selection means all statuses; otherwise only cells
    /// whose status key is picked match (an empty selection matches nothing).
    /// </summary>
    public static bool CellMatchesStatus(SubmissionCellStatus status, IReadOnlyCollection<string>? selected) =>
        selected == null || selected.Contains(StatusKey(status));

    /// <summary>
    /// Parses the Team <c>status</c> query: a comma-separated list of submitted, unsubmitted, open,
    /// notime. Empty, "all" or nothing recognised means every status.
    /// </summary>
    public static HashSet<string> ParseStatusFilter(string? text)
    {
        var known = StatusOptions.Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var picked = (text ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .Where(known.Contains)
            .ToHashSet(StringComparer.Ordinal);
        return picked.Count == 0 ? known : picked;
    }

    public static string StatusLabel(SubmissionCellStatus status) => status switch
    {
        SubmissionCellStatus.Submitted => "Submitted",
        SubmissionCellStatus.Unsubmitted => "Not submitted",
        SubmissionCellStatus.Open => "In progress",
        _ => "No time"
    };

    /// <summary>Parses a stored JSON id array. Empty on missing or malformed text.</summary>
    public static List<string> ParseIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<List<string>>(json) ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static int ParseOffset(string? text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? Math.Clamp(v, 0, MaxOffset)
            : fallback;
}

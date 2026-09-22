using System.Globalization;

namespace My.Shared.Rules;

/// <summary>
/// Groups already-localized stopwatch sessions into calendar-day sections, then
/// work-item rows. Used by the Stopwatch Day view.
/// </summary>
public static class StopwatchDayViewRules
{
    public const int MaxRangeDays = 10;

    public sealed class SessionSlice
    {
        public required string TaskId { get; init; }
        public required string StopwatchItemId { get; init; }
        /// <summary>Calendar day to group under (usually start's local date; today if a live session started outside the visible week).</summary>
        public required DateTime Day { get; init; }
        public required DateTime StartLocal { get; init; }
        public DateTime? EndLocal { get; init; }
        /// <summary>UTC start for live elapsed (StopwatchRules.ElapsedForActiveSession).</summary>
        public required DateTime StartUtc { get; init; }
        public required TimeSpan Duration { get; init; }
        public required bool IsLocked { get; init; }
        public required bool IsRunning { get; init; }
    }

    public sealed class ItemRow
    {
        public required DateTime Day { get; init; }
        public required string StopwatchItemId { get; init; }
        public required TimeSpan CompletedDuration { get; init; }
        public required IReadOnlyList<SessionSlice> Sessions { get; init; }
    }

    public sealed class DaySection
    {
        public required DateTime Day { get; init; }
        public required IReadOnlyList<ItemRow> Items { get; init; }
    }

    public static IReadOnlyList<DaySection> GroupByDayThenItem(IEnumerable<SessionSlice> sessions)
    {
        return sessions
            .GroupBy(s => s.Day.Date)
            .OrderByDescending(g => g.Key)
            .Select(day => new DaySection
            {
                Day = day.Key,
                Items = day
                    .GroupBy(s => s.StopwatchItemId)
                    .Select(ig =>
                    {
                        var list = ig.OrderBy(s => s.StartLocal).ToList();
                        return new ItemRow
                        {
                            Day = day.Key,
                            StopwatchItemId = ig.Key,
                            CompletedDuration = TimeSpan.FromTicks(
                                list.Where(s => !s.IsRunning).Sum(s => s.Duration.Ticks)),
                            Sessions = list
                        };
                    })
                    .OrderByDescending(r => r.Sessions.Max(s => s.StartLocal))
                    .ToList()
            })
            .ToList();
    }

    public static bool TryParseUtcRange(string? fromRaw, string? toRaw, out DateTime fromUtc, out DateTime toUtc, out string? error)
    {
        fromUtc = default;
        toUtc = default;
        error = null;
        if (!DateTimeOffset.TryParse(fromRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromOff)
            || !DateTimeOffset.TryParse(toRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toOff))
        {
            error = "from and to (UTC) are required.";
            return false;
        }

        fromUtc = fromOff.UtcDateTime;
        toUtc = toOff.UtcDateTime;
        return TryValidateRange(fromUtc, toUtc, out error);
    }

    public static bool TryValidateRange(DateTime fromUtc, DateTime toUtcExclusive, out string? error)
    {
        error = null;
        if (toUtcExclusive <= fromUtc)
        {
            error = "The Day view range end must be after the start.";
            return false;
        }

        if ((toUtcExclusive - fromUtc).TotalDays > MaxRangeDays)
        {
            error = $"The Day view range can be at most {MaxRangeDays} days.";
            return false;
        }

        return true;
    }
}

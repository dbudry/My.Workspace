namespace My.Shared.Rules;

/// <summary>
/// Per-day hour totals for the Calendar view. Sums the chips already painted on a day
/// so the footer matches what the user sees (no extra API). Overlay chips are skipped
/// when the original for the same task is also present (Both mode).
/// </summary>
public static class CalendarDayTotalRules
{
    public readonly record struct Chip(
        DateTime Start,
        TimeSpan Duration,
        bool IsAllDay,
        bool IsOverlay,
        string TaskId);

    public static TimeSpan HoursForChip(Chip chip, double workdayHours)
    {
        if (chip.IsAllDay)
        {
            var day = chip.Start.Date;
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                return TimeSpan.Zero;
            var hours = workdayHours > 0 ? workdayHours : AllDayEntryRules.DefaultWorkdayHours;
            return TimeSpan.FromHours(hours);
        }

        return chip.Duration < TimeSpan.Zero ? TimeSpan.Zero : chip.Duration;
    }

    public static IReadOnlyDictionary<DateOnly, TimeSpan> SumByDay(
        IEnumerable<Chip> chips,
        double workdayHours)
    {
        var totals = new Dictionary<DateOnly, TimeSpan>();
        foreach (var dayGroup in chips.GroupBy(c => DateOnly.FromDateTime(c.Start.Date)))
        {
            var list = dayGroup.ToList();
            var originalIds = list
                .Where(c => !c.IsOverlay)
                .Select(c => c.TaskId)
                .ToHashSet(StringComparer.Ordinal);

            var sum = TimeSpan.Zero;
            foreach (var chip in list)
            {
                if (chip.IsOverlay && originalIds.Contains(chip.TaskId))
                    continue;
                sum += HoursForChip(chip, workdayHours);
            }

            if (sum > TimeSpan.Zero)
                totals[dayGroup.Key] = sum;
        }

        return totals;
    }

    public static string Format(TimeSpan duration) => WeekEntryGridRules.FormatDuration(duration);

    public static IReadOnlyDictionary<string, string> ToIsoLabels(
        IReadOnlyDictionary<DateOnly, TimeSpan> totals)
    {
        var map = new Dictionary<string, string>(totals.Count, StringComparer.Ordinal);
        foreach (var kv in totals)
            map[kv.Key.ToString("yyyy-MM-dd")] = Format(kv.Value);
        return map;
    }
}

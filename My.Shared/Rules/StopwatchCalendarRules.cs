namespace My.Shared.Rules
{
    /// <summary>
    /// Groups stopwatch sessions for calendar display — one chip per work item per day.
    /// </summary>
    public static class StopwatchCalendarRules
    {
        public sealed class SessionSlice
        {
            public required string TaskId { get; init; }
            public required string StopwatchItemId { get; init; }
            public required string Name { get; init; }
            public required DateTime StartDate { get; init; }
            public DateTime? EndDate { get; init; }
            public TimeSpan Duration { get; init; }
            public bool IsLocked { get; init; }
        }

        public sealed class GroupedDayEntry
        {
            public required string StopwatchItemId { get; init; }
            public required string Name { get; init; }
            public required DateTime Day { get; init; }
            public required DateTime Start { get; init; }
            public required DateTime End { get; init; }
            public required TimeSpan TotalDuration { get; init; }
            public required int SessionCount { get; init; }
            public required string RepresentativeTaskId { get; init; }
            public bool IsLocked { get; init; }
        }

        public static DateTime GetSessionDisplayEnd(DateTime start, DateTime? end, TimeSpan duration)
        {
            if (!end.HasValue)
                return start;

            return duration > TimeSpan.Zero ? start.Add(duration) : end.Value;
        }

        public static IEnumerable<GroupedDayEntry> GroupByWorkItemAndDay(IEnumerable<SessionSlice> sessions)
        {
            return sessions
                .GroupBy(s => (s.StopwatchItemId, Day: s.StartDate.Date))
                .Select(g =>
                {
                    var list = g.ToList();
                    var start = list.Min(s => s.StartDate);
                    var end = list.Max(s => GetSessionDisplayEnd(s.StartDate, s.EndDate, s.Duration));
                    if (end <= start)
                        end = start.AddMinutes(15);

                    return new GroupedDayEntry
                    {
                        StopwatchItemId = g.Key.StopwatchItemId,
                        Name = list[0].Name,
                        Day = g.Key.Day,
                        Start = start,
                        End = end,
                        TotalDuration = TimeSpan.FromTicks(list.Sum(s => s.Duration.Ticks)),
                        SessionCount = list.Count,
                        RepresentativeTaskId = list.OrderByDescending(s => s.StartDate).First().TaskId,
                        IsLocked = list.Any(s => s.IsLocked)
                    };
                });
        }
    }
}
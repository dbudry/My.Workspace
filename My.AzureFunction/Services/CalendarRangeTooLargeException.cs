namespace My.Functions.Services;

/// <summary>
/// Thrown by <see cref="GoogleCalendarService.ListEventsInRangeAsync"/> when the
/// requested date range expands (after <c>singleEvents=true</c>) to more events
/// than the safety cap allows. Used by the pull endpoint and the nightly timer
/// to return a friendly DTO error instead of timing out the Function App.
/// </summary>
public class CalendarRangeTooLargeException : Exception
{
    public int InstanceCount { get; }
    public int Limit { get; }

    public CalendarRangeTooLargeException(int instanceCount, int limit)
        : base($"Date range expanded to {instanceCount}+ events (cap: {limit}). " +
               $"Narrow the range or split it into smaller pulls — recurring events on " +
               $"a long range often blow past this. Consider [-7d, +7d] as a starting point.")
    {
        InstanceCount = instanceCount;
        Limit = limit;
    }
}

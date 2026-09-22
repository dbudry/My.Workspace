using My.Shared.Dtos.TrackedTask;

namespace My.Shared.Dtos.StopwatchItem;

/// <summary>
/// Stopwatch sessions in a date range plus the work items they belong to.
/// The client groups these by the user's local calendar day.
/// </summary>
public class StopwatchDayViewDto
{
    public List<StopwatchItemDto> Items { get; set; } = [];
    public List<TrackedTaskDto> Sessions { get; set; } = [];
}

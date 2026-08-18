using My.Shared.Dtos.StopwatchItem;
using My.Shared.Dtos.TrackedTask;

namespace My.Shared.Dtos.TaskList
{
    /// <summary>
    /// One row in the unified Tasks list — either a grouped stopwatch work item or a manual
    /// tracked-task entry. The server merges, sorts, and pages these; the client renders the
    /// carried sub-DTO. Exactly one of <see cref="StopwatchItem"/> / <see cref="ManualTask"/> is set.
    /// </summary>
    public class TaskListRowDto
    {
        public bool IsStopwatch { get; set; }

        public StopwatchItemDto? StopwatchItem { get; set; }

        public TrackedTaskDto? ManualTask { get; set; }
    }
}

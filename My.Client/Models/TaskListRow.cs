using My.Shared.Dtos.StopwatchItem;

namespace My.Client.Models
{
    public enum TaskListRowKind
    {
        Stopwatch,
        Manual
    }

    /// <summary>
    /// One row in the unified Tasks table — either a grouped stopwatch work item or a manual entry.
    /// </summary>
    public sealed class TaskListRow
    {
        public TaskListRowKind Kind { get; init; }

        public string Name { get; init; } = null!;

        public string? ProjectDisplayName { get; init; }

        public TimeSpan Duration { get; init; }

        /// <summary>Sort key — last worked for stopwatch, start date for manual.</summary>
        public DateTime SortDate { get; init; }

        public DateTime DisplayDate { get; init; }

        public bool IsRunning { get; init; }

        public bool IsAllDay { get; init; }

        public bool IsLocked { get; init; }

        /// <summary>Alias overlay row shown alongside the employee's original entry.</summary>
        public bool IsManagerAdjustmentOverlay { get; init; }

        public bool IsManagerAdjusted { get; init; }

        public StopwatchItemDto? StopwatchItem { get; init; }

        public TrackedTask? ManualTask { get; init; }
    }
}
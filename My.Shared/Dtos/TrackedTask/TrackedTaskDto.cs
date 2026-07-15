using My.Shared.Dtos.Project;

namespace My.Shared.Dtos.TrackedTask
{
    public class TrackedTaskDto
    {
        public string TaskId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public TimeSpan Duration { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsAllDay { get; set; }
        public string? ProjectId { get; set; }
        public ProjectDto? Project { get; set; }
        public bool IsMonthSubmitted { get; set; }
        public string UserId { get; set; } = null!;
        public ApplicationUserDto User { get; set; } = null!;

        /// <summary>
        /// When set, this row is a stopwatch session. Null for manual/calendar entries.
        /// </summary>
        public string? StopwatchItemId { get; set; }

        /// <summary>True when a manager alias or direct correction exists for this task.</summary>
        public bool IsManagerAdjusted { get; set; }

        /// <summary>Alias or Direct when <see cref="IsManagerAdjusted"/> is true.</summary>
        public string? AdjustmentKind { get; set; }

        /// <summary>Alias overlay values — employee sees this alongside their original entry.</summary>
        public ManagerAdjustmentDto? ManagerAdjustment { get; set; }
    }
}

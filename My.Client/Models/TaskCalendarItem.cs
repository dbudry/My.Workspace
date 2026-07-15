using Heron.MudCalendar;

namespace My.Client.Models
{
    public class TaskCalendarItem : CalendarItem
    {
        public string TaskId { get; set; } = null!;
        public string? ProjectName { get; set; }
        public string? OrganizationName { get; set; }
        public string? OrganizationColor { get; set; }
        public string? ProjectGroupName { get; set; }
        public string? ProjectGroupColor { get; set; }
        public TimeSpan Duration { get; set; }
        public string? ProjectId { get; set; }
        public bool IsLocked { get; set; }

        public bool IsManagerAdjusted { get; set; }

        public bool IsManagerAdjustmentOverlay { get; set; }

        /// <summary>True when this chip represents grouped stopwatch sessions (not a manual entry).</summary>
        public bool IsStopwatchGroup { get; set; }

        public string? StopwatchItemId { get; set; }

        /// <summary>Local calendar day for filtering sessions when the chip is clicked.</summary>
        public DateTime? StopwatchDay { get; set; }

        public int SessionCount { get; set; }
    }
}

namespace My.Client.Models
{
    /// <summary>
    /// Represents an active timer entry in the Task Timer.
    /// Task data is persisted in the database (source of truth).
    /// Local running-state (which entry is ticking) is cached in localStorage.
    /// </summary>
    public class TimerEntry
    {
        /// <summary>API-assigned task ID. Used as the unique identifier.</summary>
        public string? TaskId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? ProjectId { get; set; }

        public string? ProjectDisplayName { get; set; }

        /// <summary>Inherited organization name (for the color-bar tooltip).</summary>
        public string? OrganizationName { get; set; }

        /// <summary>Inherited organization color (for the user's color-source preference).</summary>
        public string? OrganizationColor { get; set; }

        /// <summary>Inherited project group name (for the color-bar tooltip).</summary>
        public string? ProjectGroupName { get; set; }

        /// <summary>Inherited project group color (for the user's color-source preference).</summary>
        public string? ProjectGroupColor { get; set; }

        /// <summary>When this task was originally started.</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>When the current running segment started. Null if paused.</summary>
        public DateTime? SegmentStartedAt { get; set; }

        /// <summary>Total accumulated duration from previous segments (not including current running segment).</summary>
        public TimeSpan AccumulatedDuration { get; set; }

        /// <summary>Whether this entry's timer is currently running.</summary>
        public bool IsRunning { get; set; }

        /// <summary>Calculates live elapsed time including any current running segment.</summary>
        public TimeSpan GetElapsed()
        {
            if (IsRunning && SegmentStartedAt.HasValue)
                return AccumulatedDuration + (DateTime.Now - SegmentStartedAt.Value);

            return AccumulatedDuration;
        }
    }
}

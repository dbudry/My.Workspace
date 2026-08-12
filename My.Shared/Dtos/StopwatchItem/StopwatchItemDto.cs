using My.Shared.Dtos.Project;

namespace My.Shared.Dtos.StopwatchItem
{
    public class StopwatchItemDto
    {
        public string StopwatchItemId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? ProjectId { get; set; }
        public ProjectDto? Project { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public bool IsRunning { get; set; }
        public string? ActiveSessionId { get; set; }
        public DateTime? ActiveSessionStartDate { get; set; }
        public DateTime LastWorkedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// True when any session falls in a submitted (locked) month. Name/project on the work
        /// item cannot change while this is true; unlocked sessions can still edit duration.
        /// </summary>
        public bool HasLockedSessions { get; set; }
    }
}
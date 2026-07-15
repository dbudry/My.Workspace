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
    }
}
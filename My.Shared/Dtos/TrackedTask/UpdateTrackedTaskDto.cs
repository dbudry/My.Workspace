using My.Shared.Dtos.Project;

namespace My.Shared.Dtos.TrackedTask
{
    public class UpdateTrackedTaskDto
    {
        public string TaskId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public TimeSpan? Duration { get; set; }
        public bool IsAllDay { get; set; }
        public string? ProjectId { get; set; }
        public ProjectDto? Project { get; set; }
    }
}

namespace My.DAL.Models
{
    /// <summary>
    /// Persistent stopwatch work item. Accumulates <see cref="TrackedTask"/> sessions over time.
    /// </summary>
    public class StopwatchItem
    {
        public string StopwatchItemId { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public ApplicationUser User { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? ProjectId { get; set; }
        public Project? Project { get; set; }
        public DateTime LastWorkedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public ICollection<TrackedTask> Sessions { get; set; } = new List<TrackedTask>();
    }
}
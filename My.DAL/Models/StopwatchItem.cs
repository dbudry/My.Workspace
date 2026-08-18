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
        public string Details { get; set; } = null!;
        public string? ProjectId { get; set; }
        public Project? Project { get; set; }
        public DateTime LastWorkedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// True once the user has removed this item from their Work Items list. Cleared items
        /// keep every session intact (time isn't touched) — they're just excluded from
        /// GetStopwatchItems going forward. There's no "un-clear" path yet: this is meant as a
        /// one-way "I'm done seeing this here" action, distinct from actually deleting the item
        /// and its sessions (see StopwatchItemFunction.DeleteStopwatchItemAsync).
        /// </summary>
        public bool IsCleared { get; set; }

        public ICollection<TrackedTask> Sessions { get; set; } = new List<TrackedTask>();
    }
}
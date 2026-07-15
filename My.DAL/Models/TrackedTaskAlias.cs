namespace My.DAL.Models
{
    /// <summary>
    /// Manager/admin override of a user's submitted TrackedTask. Visible only to
    /// Manager:Tyme / Admin:Tyme / global Admin. One alias per TaskId; deleting the
    /// alias reverts to the original task's values.
    /// </summary>
    public class TrackedTaskAlias
    {
        public string TrackedTaskAliasId { get; set; } = null!;

        public string TaskId { get; set; } = null!;
        public TrackedTask Task { get; set; } = null!;

        /// <summary>Manager override of the task description (TrackedTask.Name).</summary>
        public string Name { get; set; } = null!;

        public DateTime StartDate { get; set; }
        public TimeSpan Duration { get; set; }
        public string? ProjectId { get; set; }
        public Project? Project { get; set; }

        /// <summary>
        /// Manager override of billable status — independent of the aliased project's
        /// <see cref="Project.IsBillable"/> flag. The employee's original
        /// <see cref="TrackedTask.IsBillable"/> is unchanged on the underlying task.
        /// </summary>
        public bool IsBillable { get; set; }

        public string CreatedByUserId { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}

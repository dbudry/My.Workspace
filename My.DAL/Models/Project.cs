namespace My.DAL.Models
{
    /// <summary>
    /// Workspace-shared reference data. Not owned by any single user — anyone in the
    /// workspace with appropriate role can log time against it. Slug uniqueness is
    /// workspace-wide.
    /// </summary>
    public class Project
    {
        public string ProjectId { get; set; } = null!;
        public string Name { get; set; } = null!;

        /// <summary>
        /// Public-facing name used on shared surfaces (today: the workspace Team Availability
        /// calendar). When null, those surfaces fall back to <see cref="Name"/>. Lets a manager
        /// keep an internal name like "Q2 Vacation" while the team sees a generic
        /// "Out of Office".
        /// </summary>
        public string? DisplayName { get; set; }

        public string? Slug { get; set; }
        public string? OrganizationId { get; set; }
        public Organization? Organization { get; set; }
        public string? DepartmentId { get; set; }
        public Department? Department { get; set; }
        public string? ProjectGroupId { get; set; }
        public ProjectGroup? ProjectGroup { get; set; }
        public ICollection<TrackedTask>? TrackedTasks { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsArchived { get; set; }

        /// <summary>
        /// When true, time logged against this project is also echoed onto a shared
        /// "Team Availability" Google calendar as a sanitized sister event
        /// ("[&lt;FullName&gt;] &lt;ProjectName&gt;") so the team can see who's out.
        /// Manager-set on the project edit form; per-user OAuth still writes the event.
        /// </summary>
        public bool IsSharedAvailability { get; set; }

        /// <summary>
        /// When true, time logged against this project is marked billable on each
        /// <see cref="TrackedTask"/>. Manager-set on the project edit form; employees
        /// do not choose billable per entry. Mutually exclusive with
        /// <see cref="IsSharedAvailability"/> (availability/PTO projects are never billable).
        /// </summary>
         public bool IsBillable { get; set; }
    }
}

namespace My.Shared.Dtos.Project
{
    public class ProjectDto
    {
        public string ProjectId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? DisplayName { get; set; }
        public string? Slug { get; set; }
        public string? OrganizationId { get; set; }
        public string? OrganizationName { get; set; }
        public string? OrganizationColor { get; set; }
        public string? DepartmentId { get; set; }
        public string? DepartmentName { get; set; }
        public string? ProjectGroupId { get; set; }
        public string? ProjectGroupName { get; set; }
        public string? ProjectGroupColor { get; set; }
        public bool IsActive { get; set; }
        public bool IsArchived { get; set; }

        /// <summary>
        /// When true, time logged against this project is echoed onto the workspace's
        /// "Team Availability" Google calendar. Manager-set on the project edit form.
        /// </summary>
        public bool IsSharedAvailability { get; set; }

        /// <summary>
        /// When true, time logged against this project is marked billable. Manager-set
        /// on the project edit form.
        /// </summary>
        public bool IsBillable { get; set; }
    }
}

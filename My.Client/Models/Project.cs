using System.ComponentModel.DataAnnotations;
using My.Shared.Dtos.Project;
using My.Shared.Validation;

namespace My.Client.Models
{
    public class Project
    {
        public string ProjectId { get; set; } = null!;

        [Required]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Name can not have less then 3 characters and more then 50.")]
        public string Name { get; set; } = null!;

        [StringLength(SlugRules.MaxLength, MinimumLength = SlugRules.MinLength,
            ErrorMessage = "Slug must be between 2 and 10 characters.")]
        [RegularExpression("^[a-z0-9]+$", ErrorMessage = "Slug may only contain lowercase letters and digits (no spaces or dashes).")]
        public string? Slug { get; set; }

        /// <summary>
        /// Public-facing name shown on shared surfaces (today: the workspace Team
        /// Availability calendar). Null = fall back to <see cref="Name"/>. Distinct
        /// from the in-app computed <see cref="DisplayName"/> which prefixes by group.
        /// </summary>
        [StringLength(100, ErrorMessage = "Display name can't exceed 100 characters.")]
        public string? TeamDisplayName { get; set; }

        public string? OrganizationId { get; set; }
        public string? OrganizationName { get; set; }
        public string? OrganizationColor { get; set; }

        public string? DepartmentId { get; set; }
        public string? DepartmentName { get; set; }

        public string? ProjectGroupId { get; set; }
        public string? ProjectGroupName { get; set; }
        public string? ProjectGroupColor { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsArchived { get; set; }

        /// <summary>
        /// When true, time logged against this project is echoed on the workspace's
        /// "Team Availability" Google calendar so the team can see who's out. Set by
        /// a Tyme manager on the project edit form.
        /// </summary>
        public bool IsSharedAvailability { get; set; }

        /// <summary>
        /// When true, time logged against this project is marked billable. Set by a
        /// Tyme manager on the project edit form.
        /// </summary>
        public bool IsBillable { get; set; }

        /// <summary>The project's human name with its group prefix — e.g. "Profit Network - Worker".</summary>
        public string DisplayName => string.IsNullOrEmpty(ProjectGroupName)
            ? Name
            : $"{ProjectGroupName} - {Name}";

        /// <summary>Calendar tag — what the user types in `[…]` in a Google Calendar event
        /// title to route to this project. Now just the project's own slug; the group
        /// prefix was retired when projects became workspace-shared.</summary>
        public string? FullSlug => string.IsNullOrEmpty(Slug) ? null : Slug;

        /// <summary>What we match against when the user types in an autocomplete.</summary>
        public string SearchText =>
            $"{Name} {Slug} {ProjectGroupName}".Trim();

        public Project()
        {

        }

        public Project(ProjectDto project)
        {
            ProjectId = project.ProjectId;
            Name = project.Name;
            TeamDisplayName = project.DisplayName;
            Slug = project.Slug;
            OrganizationId = project.OrganizationId;
            OrganizationName = project.OrganizationName;
            OrganizationColor = project.OrganizationColor;
            DepartmentId = project.DepartmentId;
            DepartmentName = project.DepartmentName;
            ProjectGroupId = project.ProjectGroupId;
            ProjectGroupName = project.ProjectGroupName;
            ProjectGroupColor = project.ProjectGroupColor;
            IsActive = project.IsActive;
            IsArchived = project.IsArchived;
            IsSharedAvailability = project.IsSharedAvailability;
            IsBillable = project.IsBillable;
        }
    }
}

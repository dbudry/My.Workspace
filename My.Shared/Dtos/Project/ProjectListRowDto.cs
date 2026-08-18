using System.Text.Json.Serialization;

namespace My.Shared.Dtos.Project
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ProjectListRowKind
    {
        Organization,
        ProjectGroup,
        UnassignedBucket,
        Project
    }

    /// <summary>
    /// One row in the manager projects table: a parent header (organization, project
    /// group, or unassigned bucket) or a project nested under the preceding header.
    /// </summary>
    public class ProjectListRowDto
    {
        public ProjectListRowKind Kind { get; set; }
        public ProjectDto? Project { get; set; }
        public ProjectListOrganizationHeaderDto? Organization { get; set; }
        public ProjectListGroupHeaderDto? ProjectGroup { get; set; }
        public string? BucketLabel { get; set; }
        public int? ProjectCount { get; set; }
        public bool ProjectsTruncated { get; set; }
    }

    public class ProjectListOrganizationHeaderDto
    {
        public string OrganizationId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Color { get; set; }
        public int ProjectCount { get; set; }
        public bool ProjectsTruncated { get; set; }
    }

    public class ProjectListGroupHeaderDto
    {
        public string ProjectGroupId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Color { get; set; }
        public int ProjectCount { get; set; }
        public bool ProjectsTruncated { get; set; }
    }
}
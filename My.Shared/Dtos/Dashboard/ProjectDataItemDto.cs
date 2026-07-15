namespace My.Shared.Dtos.Dashboard
{
    public class ProjectDataItemDto
    {
        public string ProjectId { get; set; } = null!;
        public string ProjectName { get; set; } = null!;
        public TimeSpan Time { get; set; }

        // Parent IDs/names so the dashboard chart can re-pivot by Organization or
        // Project Group on the client without another round trip.
        public string? OrganizationId { get; set; }
        public string? OrganizationName { get; set; }
        public string? OrganizationColor { get; set; }
        public string? ProjectGroupId { get; set; }
        public string? ProjectGroupName { get; set; }
        public string? ProjectGroupColor { get; set; }
    }
}

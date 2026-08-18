namespace My.Shared.Dtos.Project
{
    public class CreateProjectDto
    {
        public string Name { get; set; } = null!;
        public string? DisplayName { get; set; }
        public string? Slug { get; set; }
        public string? OrganizationId { get; set; }
        public string? DepartmentId { get; set; }
        public string? ProjectGroupId { get; set; }
        public bool IsSharedAvailability { get; set; }
        public bool IsBillable { get; set; }
    }
}

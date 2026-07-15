namespace My.Shared.Dtos.Department
{
    public class UpdateDepartmentDto
    {
        public string DepartmentId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string OrganizationId { get; set; } = null!;
    }
}

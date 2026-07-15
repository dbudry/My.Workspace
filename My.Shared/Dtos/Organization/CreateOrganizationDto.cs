namespace My.Shared.Dtos.Organization
{
    public class CreateOrganizationDto
    {
        public string Name { get; set; } = null!;
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public string? Note { get; set; }
        public string? Color { get; set; }
    }
}

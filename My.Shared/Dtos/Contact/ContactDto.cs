namespace My.Shared.Dtos.Contact
{
    public class ContactDto
    {
        public string ContactId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Title { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? ContactType { get; set; }
        public string? OrganizationId { get; set; }
        public string? DepartmentId { get; set; }
    }
}

using My.Shared.Dtos.Contact;

namespace My.Shared.Dtos.Organization
{
    public class OrganizationDto
    {
        public string OrganizationId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public string? Note { get; set; }
        public string? Color { get; set; }
        public bool IsActive { get; set; }
        public bool IsArchived { get; set; }
        /// <summary>Populated on list endpoints without hydrating contact rows.</summary>
        public int ContactCount { get; set; }
        /// <summary>Populated on list endpoints without hydrating department rows.</summary>
        public int DepartmentCount { get; set; }
        public List<ContactDto>? Contacts { get; set; }
        public List<DepartmentDto>? Departments { get; set; }
    }

    public class DepartmentDto
    {
        public string DepartmentId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string OrganizationId { get; set; } = null!;
        public bool IsActive { get; set; }
        public bool IsArchived { get; set; }
        public List<ContactDto>? Contacts { get; set; }
    }
}

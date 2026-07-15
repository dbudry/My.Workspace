using System.ComponentModel.DataAnnotations;
using My.Shared.Dtos.Contact;
using My.Shared.Dtos.Organization;

namespace My.Client.Models
{
    public class Organization
    {
        public string OrganizationId { get; set; } = null!;

        [Required]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Organization name must be between 2 and 100 characters.")]
        public string Name { get; set; } = null!;

        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public string? Note { get; set; }
        public string? Color { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsArchived { get; set; }
        public int ContactCount { get; set; }
        public int DepartmentCount { get; set; }
        public List<ContactModel>? Contacts { get; set; }
        public List<DepartmentModel>? Departments { get; set; }

        public Organization()
        {
        }

        public Organization(OrganizationDto dto)
        {
            OrganizationId = dto.OrganizationId;
            Name = dto.Name;
            Address = dto.Address;
            City = dto.City;
            State = dto.State;
            PostalCode = dto.PostalCode;
            Country = dto.Country;
            Note = dto.Note;
            Color = dto.Color;
            IsActive = dto.IsActive;
            IsArchived = dto.IsArchived;
            ContactCount = dto.ContactCount;
            DepartmentCount = dto.DepartmentCount;
            Contacts = dto.Contacts?.Select(c => new ContactModel(c)).ToList();
            Departments = dto.Departments?.Select(d => new DepartmentModel(d)).ToList();
        }
    }

    public class DepartmentModel
    {
        public string DepartmentId { get; set; } = null!;

        [Required]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Department name must be between 2 and 100 characters.")]
        public string Name { get; set; } = null!;

        public string OrganizationId { get; set; } = null!;
        public bool IsActive { get; set; } = true;
        public bool IsArchived { get; set; }
        public List<ContactModel>? Contacts { get; set; }

        public DepartmentModel()
        {
        }

        public DepartmentModel(DepartmentDto dto)
        {
            DepartmentId = dto.DepartmentId;
            Name = dto.Name;
            OrganizationId = dto.OrganizationId;
            IsActive = dto.IsActive;
            IsArchived = dto.IsArchived;
            Contacts = dto.Contacts?.Select(c => new ContactModel(c)).ToList();
        }
    }

    public class ContactModel
    {
        public string ContactId { get; set; } = null!;

        [Required]
        [StringLength(100, MinimumLength = 1, ErrorMessage = "Contact name is required.")]
        public string Name { get; set; } = null!;

        public string? Title { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? ContactType { get; set; }
        public string? OrganizationId { get; set; }
        public string? DepartmentId { get; set; }

        public ContactModel()
        {
        }

        public ContactModel(ContactDto dto)
        {
            ContactId = dto.ContactId;
            Name = dto.Name;
            Title = dto.Title;
            PhoneNumber = dto.PhoneNumber;
            Email = dto.Email;
            ContactType = dto.ContactType;
            OrganizationId = dto.OrganizationId;
            DepartmentId = dto.DepartmentId;
        }
    }
}

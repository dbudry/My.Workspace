using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    public class Department
    {
        public string DepartmentId { get; set; } = null!;

        [Required, MaxLength(100)]
        public string Name { get; set; } = null!;

        public string OrganizationId { get; set; } = null!;
        public Organization Organization { get; set; } = null!;

        public bool IsActive { get; set; } = true;

        public bool IsArchived { get; set; }

        public ICollection<Contact>? Contacts { get; set; }

        public ICollection<Project>? Projects { get; set; }
    }
}

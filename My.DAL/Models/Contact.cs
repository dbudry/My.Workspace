using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    public class Contact
    {
        public string ContactId { get; set; } = null!;

        [Required, MaxLength(100)]
        public string Name { get; set; } = null!;

        [MaxLength(100)]
        public string? Title { get; set; }

        [MaxLength(30)]
        public string? PhoneNumber { get; set; }

        [MaxLength(256)]
        public string? Email { get; set; }

        [MaxLength(50)]
        public string? ContactType { get; set; }

        public string? OrganizationId { get; set; }
        public Organization? Organization { get; set; }

        public string? DepartmentId { get; set; }
        public Department? Department { get; set; }
    }
}

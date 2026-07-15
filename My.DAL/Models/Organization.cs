using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    public class Organization
    {
        public string OrganizationId { get; set; } = null!;

        [Required, MaxLength(100)]
        public string Name { get; set; } = null!;

        [MaxLength(255)]
        public string? Address { get; set; }

        [MaxLength(50)]
        public string? City { get; set; }

        [MaxLength(50)]
        public string? State { get; set; }

        [MaxLength(20)]
        public string? PostalCode { get; set; }

        [MaxLength(50)]
        public string? Country { get; set; }

        public string? Note { get; set; }

        /// <summary>
        /// Auto-assigned random hex (e.g. "#1976d2") used as the visual indicator for
        /// projects belonging to this organization that have no Project Group of their own.
        /// </summary>
        [MaxLength(9)]
        public string? Color { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsArchived { get; set; }

        public ICollection<Contact>? Contacts { get; set; }

        public ICollection<Department>? Departments { get; set; }

        public ICollection<Project>? Projects { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;

namespace My.Shared.Dtos.Intranet
{
    public class UpdateIntranetDocumentDto
    {
        [StringLength(500)]
        public string? Name { get; set; }

        public string? Description { get; set; }

        [StringLength(100)]
        public string? Category { get; set; }

        public bool? IsFeatured { get; set; }
        public bool? IsActive { get; set; }
    }
}

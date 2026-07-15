using System.ComponentModel.DataAnnotations;

namespace My.Shared.Dtos.Intranet
{
    public class UpdateIntranetNavigationItemDto
    {
        [Required]
        [StringLength(100, MinimumLength = 1)]
        public string Title { get; set; } = null!;

        [StringLength(50)]
        public string? Icon { get; set; }

        public string? PageId { get; set; }

        [StringLength(500)]
        public string? ExternalUrl { get; set; }

        public int SortOrder { get; set; }

        public string? ParentId { get; set; }

        public bool IsVisible { get; set; }
    }
}

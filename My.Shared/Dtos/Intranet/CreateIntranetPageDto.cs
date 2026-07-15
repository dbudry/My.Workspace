using System.ComponentModel.DataAnnotations;

namespace My.Shared.Dtos.Intranet
{
    public class CreateIntranetPageDto
    {
        [Required]
        [StringLength(200, MinimumLength = 1)]
        public string Title { get; set; } = null!;

        [StringLength(200)]
        public string? Slug { get; set; }

        public string? ParentPageId { get; set; }

        /// <summary>
        /// Markdown content.
        /// </summary>
        public string? ContentMarkdown { get; set; }

        public int SortOrder { get; set; }

        public bool IsPublished { get; set; } = true;
    }
}

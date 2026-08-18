namespace My.Shared.Dtos.Intranet
{
    /// <summary>
    /// DTO for displaying an intranet page, including hierarchy info and attached documents.
    /// </summary>
    public class IntranetPageDto
    {
        public string PageId { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string? Slug { get; set; }

        public string? ParentPageId { get; set; }
        public string? ParentPageTitle { get; set; }

        public string? ContentMarkdown { get; set; }

        public int SortOrder { get; set; }
        public bool IsPublished { get; set; }

        public string? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }

        public string? UpdatedByUserId { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Drive documents attached to this page (ordered).
        /// </summary>
        public List<IntranetPageDocumentDto> Documents { get; set; } = new();

        /// <summary>
        /// Child pages for tree navigation (lightweight).
        /// </summary>
        public List<IntranetPageSummaryDto> ChildPages { get; set; } = new();
    }

    /// <summary>
    /// Lightweight summary for tree views, breadcrumbs, child lists, etc.
    /// </summary>
    public class IntranetPageSummaryDto
    {
        public string PageId { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string? Slug { get; set; }
        public int SortOrder { get; set; }
        public bool IsPublished { get; set; }
    }
}

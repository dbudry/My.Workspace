namespace My.Shared.Dtos.Intranet
{
    /// <summary>
    /// Lightweight reference to a page that uses a library document.
    /// </summary>
    public class IntranetDocumentPageUsageDto
    {
        public string PageId { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string? Caption { get; set; }

        /// <summary>True when the file id appears in the page's saved HTML content.</summary>
        public bool IsReferencedInContent { get; set; }
    }
}
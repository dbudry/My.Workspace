namespace My.DAL.Models
{
    /// <summary>
    /// Junction table allowing an IntranetPage to reference multiple curated Drive documents.
    /// Supports ordering within the page and an optional caption (e.g. "Q3 Financial Report").
    /// This enables the "mini site builder" experience of embedding/linking documents inside content pages.
    /// </summary>
    public class IntranetPageDocument
    {
        public string PageId { get; set; } = null!;
        public IntranetPage Page { get; set; } = null!;

        public string DocumentId { get; set; } = null!;
        public IntranetDocument Document { get; set; } = null!;

        public int SortOrder { get; set; }

        /// <summary>
        /// Optional caption or context shown with the document link/embed on the page.
        /// </summary>
        [System.ComponentModel.DataAnnotations.MaxLength(300)]
        public string? Caption { get; set; }
    }
}

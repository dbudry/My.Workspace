namespace My.DAL.Models
{
    /// <summary>
    /// Hierarchical content page for the company intranet / knowledge base.
    /// Supports a simple "mini site builder" experience with Markdown content
    /// and attached/embedded Google Drive documents.
    /// </summary>
    public class IntranetPage
    {
        public string PageId { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MaxLength(200)]
        public string Title { get; set; } = null!;

        /// <summary>
        /// URL-friendly slug. Unique within the intranet.
        /// </summary>
        [System.ComponentModel.DataAnnotations.MaxLength(200)]
        public string? Slug { get; set; }

        public string? ParentPageId { get; set; }
        public IntranetPage? ParentPage { get; set; }

        public ICollection<IntranetPage>? ChildPages { get; set; }

        /// <summary>
        /// Markdown content for the page body. Simple and sufficient for v1 "site builder".
        /// Can evolve to a richer block/JSON structure later if needed.
        /// </summary>
        public string? ContentMarkdown { get; set; }

        /// <summary>
        /// Display order among siblings.
        /// </summary>
        public int SortOrder { get; set; }

        public bool IsPublished { get; set; } = true;

        public string? CreatedByUserId { get; set; }
        public ApplicationUser? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; }

        public string? UpdatedByUserId { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Drive documents linked/embedded on this page (with per-page ordering and captions).
        /// </summary>
        public ICollection<IntranetPageDocument>? PageDocuments { get; set; }

        // --- Editing and Access Control (less-is-more with option for control) ---

        /// <summary>
        /// When true, editing this page is restricted to the original creator (CreatedByUserId)
        /// plus any user with the Admin:Intranet role.
        /// When false (default), any user with intranet edit permissions (Admin:Intranet or Editor:Intranet)
        /// can edit the page. This provides the "option for control" on sensitive pages while keeping
        /// day-to-day editing simple and collaborative for most content.
        /// </summary>
        public bool RestrictEditingToOwner { get; set; }

        /// <summary>
        /// Visibility policy for the page.
        /// "Default" (recommended): visible to anyone who has access to the intranet module (if IsPublished).
        /// Future values (e.g. "IntranetAdminsOnly") can be added for more granular control without
        /// changing the core model.
        /// </summary>
        [System.ComponentModel.DataAnnotations.MaxLength(50)]
        public string Visibility { get; set; } = "Default";
    }
}

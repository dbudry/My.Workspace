namespace My.DAL.Models
{
    /// <summary>
    /// Curated navigation item for the main intranet navigation (top bar, sidebar, etc.).
    /// Top-level items (and their sub-items) are explicitly managed by admins with the
    /// intranet scope (e.g. Admin:Intranet). This allows reordering, hiding pages from
    /// the main nav, adding external links, and building a controlled "less is more"
    /// prominent navigation while the full page hierarchy remains available via search/tree.
    ///
    /// Favors simplicity (flat top-level + optional one level of children) but has the
    /// option for deeper control via ParentId.
    /// </summary>
    public class IntranetNavigationItem
    {
        public string Id { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MaxLength(100)]
        public string Title { get; set; } = null!;

        /// <summary>
        /// Optional icon identifier (e.g. Material icon name or custom).
        /// </summary>
        [System.ComponentModel.DataAnnotations.MaxLength(50)]
        public string? Icon { get; set; }

        /// <summary>
        /// If set, this nav item links to an internal intranet page.
        /// </summary>
        public string? PageId { get; set; }
        public IntranetPage? Page { get; set; }

        /// <summary>
        /// If set (and PageId is null), this is an external or special link (e.g. /help, mailto:, https://...).
        /// </summary>
        [System.ComponentModel.DataAnnotations.MaxLength(500)]
        public string? ExternalUrl { get; set; }

        public int SortOrder { get; set; }

        /// <summary>
        /// Parent for sub-navigation items. Supports building dropdowns or nested menus.
        /// Null = top-level link (the ones "created by admin of the intranet scope").
        /// </summary>
        public string? ParentId { get; set; }
        public IntranetNavigationItem? Parent { get; set; }

        public ICollection<IntranetNavigationItem>? Children { get; set; }

        public bool IsVisible { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Management of navigation is restricted to intranet-scoped admins in the API layer.
    }
}

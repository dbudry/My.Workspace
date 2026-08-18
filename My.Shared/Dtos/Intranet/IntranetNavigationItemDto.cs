namespace My.Shared.Dtos.Intranet
{
    /// <summary>
    /// DTO for a curated navigation item. Supports building a tree for the main intranet navigation.
    /// Top-level items are the "prominent links" explicitly managed by intranet admins.
    /// </summary>
    public class IntranetNavigationItemDto
    {
        public string Id { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string? Icon { get; set; }

        public string? PageId { get; set; }
        public string? PageTitle { get; set; }
        public string? PageSlug { get; set; }
        /// <summary>False when the linked page exists and is unpublished (draft).</summary>
        public bool PageIsPublished { get; set; } = true;

        public string? ExternalUrl { get; set; }

        public int SortOrder { get; set; }
        public bool IsVisible { get; set; }

        /// <summary>
        /// Parent navigation item Id (for sub-items). Not shown in UI.
        /// </summary>
        public string? ParentId { get; set; }

        /// <summary>
        /// Child items for sub-menus / dropdowns.
        /// </summary>
        public List<IntranetNavigationItemDto> Children { get; set; } = new();
    }
}

namespace My.DAL.Models
{
    /// <summary>
    /// Curated registry of Google Drive files that are important to the company.
    /// Actual file binaries stay in Google Drive (the source of truth).
    /// This table stores metadata + intranet-specific annotations for fast search,
    /// filtering, tagging, and linking into pages.
    /// </summary>
    public class IntranetDocument
    {
        public string DocumentId { get; set; } = null!;

        /// <summary>
        /// Google's Drive file ID. Unique and the primary key for Drive operations.
        /// </summary>
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MaxLength(200)]
        public string DriveFileId { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MaxLength(500)]
        public string Name { get; set; } = null!;

        public string? MimeType { get; set; }

        /// <summary>
        /// Direct link the user can open in Drive.
        /// </summary>
        public string? WebViewLink { get; set; }

        public string? ThumbnailLink { get; set; }

        public long? SizeBytes { get; set; }

        public DateTime? DriveLastModified { get; set; }

        public string? DriveOwnerEmail { get; set; }

        public string? DriveOwnerName { get; set; }

        // --- Intranet-specific fields ---

        public string? Description { get; set; }

        /// <summary>
        /// Simple category for browsing/filtering the document library
        /// (e.g. "Policies", "HR", "Finance", "How-To"). Can be normalized later.
        /// </summary>
        [System.ComponentModel.DataAnnotations.MaxLength(100)]
        public string? Category { get; set; }

        public bool IsFeatured { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// When we last synced metadata from the Drive API.
        /// </summary>
        public DateTime SyncedAt { get; set; }

        public ICollection<IntranetPageDocument>? PageDocuments { get; set; }
    }
}

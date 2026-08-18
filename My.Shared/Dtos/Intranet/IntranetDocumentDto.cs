namespace My.Shared.Dtos.Intranet
{
    /// <summary>
    /// DTO for a curated intranet document (Google Drive file + intranet metadata).
    /// </summary>
    public class IntranetDocumentDto
    {
        public string DocumentId { get; set; } = null!;
        public string DriveFileId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? MimeType { get; set; }
        public string? WebViewLink { get; set; }
        public string? ThumbnailLink { get; set; }
        public long? SizeBytes { get; set; }
        public DateTime? DriveLastModified { get; set; }
        public string? DriveOwnerEmail { get; set; }
        public string? DriveOwnerName { get; set; }

        // Intranet annotations
        public string? Description { get; set; }
        public string? Category { get; set; }
        public bool IsFeatured { get; set; }
        public bool IsActive { get; set; }
        public DateTime SyncedAt { get; set; }

        /// <summary>
        /// Number of intranet pages referencing this document (when requested).
        /// </summary>
        public int UsageCount { get; set; }

        /// <summary>
        /// Pages that reference this document (populated when includeUsage=true).
        /// </summary>
        public List<IntranetDocumentPageUsageDto>? UsedOnPages { get; set; }
    }
}

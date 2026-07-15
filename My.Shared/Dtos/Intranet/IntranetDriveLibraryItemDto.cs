namespace My.Shared.Dtos.Intranet
{
    /// <summary>
    /// Unified view of a file in the company Drive folder, enriched with intranet metadata and page usage.
    /// The Drive folder is the library — this DTO merges live Drive listing with DB overlays.
    /// </summary>
    public class IntranetDriveLibraryItemDto
    {
        public string DriveFileId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? MimeType { get; set; }
        public string? WebViewLink { get; set; }
        public string? ThumbnailLink { get; set; }
        public long? SizeBytes { get; set; }
        public DateTime? DriveLastModified { get; set; }
        public string? DriveOwnerEmail { get; set; }
        public string? DriveOwnerName { get; set; }

        /// <summary>Intranet registry id, if this file has been touched/annotated before.</summary>
        public string? DocumentId { get; set; }

        public string? Description { get; set; }
        public string? Category { get; set; }
        public bool IsFeatured { get; set; }

        public int UsageCount { get; set; }
        public List<IntranetDocumentPageUsageDto>? UsedOnPages { get; set; }
    }
}
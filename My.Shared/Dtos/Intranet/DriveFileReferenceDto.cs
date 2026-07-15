namespace My.Shared.Dtos.Intranet
{
    /// <summary>
    /// Lightweight result from Google Drive search / picker operations.
    /// Used when presenting a Drive browser inside the intranet admin UI.
    /// Not stored directly — used to create/update IntranetDocument records.
    /// </summary>
    public class DriveFileReferenceDto
    {
        public string DriveFileId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? MimeType { get; set; }
        public string? WebViewLink { get; set; }
        public string? ThumbnailLink { get; set; }
        public long? SizeBytes { get; set; }
        public DateTime? LastModified { get; set; }
        public string? OwnerEmail { get; set; }
        public string? OwnerName { get; set; }

        /// <summary>
        /// True if this file is already registered in the intranet library.
        /// </summary>
        public bool IsAlreadyInIntranet { get; set; }
    }
}

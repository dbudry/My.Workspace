namespace My.Shared.Dtos.Intranet
{
    /// <summary>
    /// Represents a Drive document attached to a specific page (with ordering + caption).
    /// </summary>
    public class IntranetPageDocumentDto
    {
        public string DocumentId { get; set; } = null!;
        public string DriveFileId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? MimeType { get; set; }
        public string? WebViewLink { get; set; }
        public string? ThumbnailLink { get; set; }

        public int SortOrder { get; set; }
        public string? Caption { get; set; }
    }
}

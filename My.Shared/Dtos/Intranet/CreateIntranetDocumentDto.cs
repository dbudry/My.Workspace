using System.ComponentModel.DataAnnotations;

namespace My.Shared.Dtos.Intranet
{
    /// <summary>
    /// Used when registering/curating a Drive file into the intranet library
    /// (usually after selecting it via a Drive picker or search).
    /// </summary>
    public class CreateIntranetDocumentDto
    {
        [Required]
        public string DriveFileId { get; set; } = null!;

        [Required]
        [StringLength(500)]
        public string Name { get; set; } = null!;

        public string? MimeType { get; set; }
        public string? WebViewLink { get; set; }
        public string? ThumbnailLink { get; set; }
        public long? SizeBytes { get; set; }
        public DateTime? DriveLastModified { get; set; }
        public string? DriveOwnerEmail { get; set; }
        public string? DriveOwnerName { get; set; }

        public string? Description { get; set; }

        [StringLength(100)]
        public string? Category { get; set; }

        public bool IsFeatured { get; set; }
    }
}

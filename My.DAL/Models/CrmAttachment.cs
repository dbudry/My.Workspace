using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    /// <summary>Drive file metadata for a potential. Bytes live in the CRM Drive folder.</summary>
    public class CrmAttachment
    {
        public string CrmAttachmentId { get; set; } = null!;

        public string PotentialId { get; set; } = null!;
        public Potential? Potential { get; set; }

        public string CrmStageId { get; set; } = null!;
        public CrmStage? Stage { get; set; }

        [Required, MaxLength(200)]
        public string OriginalFileName { get; set; } = null!;

        [Required, MaxLength(100)]
        public string MimeType { get; set; } = null!;

        public int SizeBytes { get; set; }

        [Required, MaxLength(128)]
        public string DriveFileId { get; set; } = null!;

        public DateTimeOffset UploadedAt { get; set; }

        [MaxLength(450)]
        public string UploadedByUserId { get; set; } = null!;
    }
}

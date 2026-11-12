using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    /// <summary>A comment on a potential. The stage is a snapshot from the moment it was posted.</summary>
    public class CrmDiscussion
    {
        public string CrmDiscussionId { get; set; } = null!;

        public string PotentialId { get; set; } = null!;
        public Potential? Potential { get; set; }

        public string CrmStageId { get; set; } = null!;
        public CrmStage? Stage { get; set; }

        [MaxLength(450)]
        public string AuthorUserId { get; set; } = null!;

        public DateTimeOffset CreatedAt { get; set; }

        [Required, MaxLength(500)]
        public string Message { get; set; } = null!;
    }
}

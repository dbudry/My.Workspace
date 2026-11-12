using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    /// <summary>A dated note on a potential, stamped with the stage it was written under.</summary>
    public class PotentialStatus
    {
        public string PotentialStatusId { get; set; } = null!;

        public string PotentialId { get; set; } = null!;
        public Potential? Potential { get; set; }

        public string CrmStageId { get; set; } = null!;
        public CrmStage? Stage { get; set; }

        public DateTime StatusDate { get; set; }

        [Required, MaxLength(500)]
        public string Body { get; set; } = null!;

        [MaxLength(450)]
        public string? CreatedByUserId { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}

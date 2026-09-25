using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    /// <summary>A note, call, meeting, or follow-up on a CRM opportunity.</summary>
    public class CrmActivity
    {
        public string CrmActivityId { get; set; } = null!;

        public string OpportunityId { get; set; } = null!;
        public Opportunity? Opportunity { get; set; }

        [Required, MaxLength(20)]
        public string ActivityType { get; set; } = null!;

        [Required, MaxLength(200)]
        public string Subject { get; set; } = null!;

        public string? Body { get; set; }

        public DateTimeOffset? DueAt { get; set; }

        public DateTimeOffset? CompletedAt { get; set; }

        /// <summary>AspNetUsers.Id. No FK, so user delete is unchanged.</summary>
        [MaxLength(450)]
        public string? OwnerUserId { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}

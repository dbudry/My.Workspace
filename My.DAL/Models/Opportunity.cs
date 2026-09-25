using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    /// <summary>
    /// A CRM deal. Not a Tyme project — time entry never reads this table.
    /// </summary>
    public class Opportunity
    {
        public string OpportunityId { get; set; } = null!;

        [Required, MaxLength(200)]
        public string Name { get; set; } = null!;

        [Required, MaxLength(20)]
        public string Stage { get; set; } = null!;

        public decimal? Amount { get; set; }

        public DateTime? ExpectedCloseDate { get; set; }

        /// <summary>AspNetUsers.Id of the deal owner. No FK, so deleting a user is unchanged.</summary>
        [MaxLength(450)]
        public string? OwnerUserId { get; set; }

        public string? OrganizationId { get; set; }
        public Organization? Organization { get; set; }

        /// <summary>
        /// Contact id, stored without a foreign key. SQL Server cannot SET NULL on both
        /// this and <see cref="OrganizationId"/> while contacts cascade from the organization.
        /// Delete paths clear the value before the contact row goes away.
        /// </summary>
        [MaxLength(450)]
        public string? ContactId { get; set; }

        public string? Note { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsArchived { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public ICollection<CrmActivity>? Activities { get; set; }
    }
}

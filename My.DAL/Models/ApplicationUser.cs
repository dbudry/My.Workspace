using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required, MaxLength(50)]
        [PersonalData]
        public string FirstName { get; set; } = null!;

        [Required, MaxLength(50)]
        [PersonalData]
        public string LastName { get; set; } = null!;

        public DateTimeOffset? LastLoginDate { get; set; }

        /// <summary>Set on each successful /api/users/provision call. Distinct from
        /// LastLoginDate (which AuthMiddleware bumps on every API request). Used as the
        /// reference timestamp for OIDC session invalidation.</summary>
        public DateTimeOffset? LastSignInAt { get; set; }

        /// <summary>Set when an admin uses "Force re-sign-in" or "Revoke Google permissions".
        /// AuthMiddleware drops the identity if LastSignInAt is null or earlier than this,
        /// forcing the user through the OIDC sign-in flow on their next API call. Cleared
        /// implicitly on the next provision (since the comparison flips).</summary>
        public DateTimeOffset? OidcSessionInvalidatedAt { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsArchived { get; set; }

        public ICollection<TrackedTask>? TrackedTasks { get; set; }
    }
}
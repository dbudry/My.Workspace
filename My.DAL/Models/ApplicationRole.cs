using Microsoft.AspNetCore.Identity;

namespace My.DAL.Models
{
    public class ApplicationRole : IdentityRole
    {
        public string Description { get; set; } = "Role.";
    }
}

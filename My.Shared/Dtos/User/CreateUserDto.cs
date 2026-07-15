using System.ComponentModel.DataAnnotations;

namespace My.Shared.Dtos.User
{
    public class CreateUserDto
    {
        [Required, MaxLength(256)]
        public string Email { get; set; } = null!;

        [Required, MaxLength(50)]
        public string FirstName { get; set; } = null!;

        [Required, MaxLength(50)]
        public string LastName { get; set; } = null!;

        [Required]
        public List<string> Roles { get; set; } = new();
    }
}

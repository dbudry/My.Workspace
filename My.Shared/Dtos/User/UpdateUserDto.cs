using System.ComponentModel.DataAnnotations;

namespace My.Shared.Dtos.User
{
    public class UpdateUserDto
    {
        [Required]
        public string Id { get; set; } = null!;

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

using System.ComponentModel.DataAnnotations;
using My.Shared.Dtos.User;

namespace My.Client.Models
{
    public class UserModel
    {
        public string Id { get; set; } = null!;

        [Required, MaxLength(256)]
        public string Email { get; set; } = null!;

        [Required, MaxLength(50)]
        public string FirstName { get; set; } = null!;

        [Required, MaxLength(50)]
        public string LastName { get; set; } = null!;

        public List<string> Roles { get; set; } = new();

        public IReadOnlyCollection<string> SelectedRoles
        {
            get => Roles;
            set => Roles = value.ToList();
        }

        public DateTimeOffset? LastLoginDate { get; set; }

        public bool IsActive { get; set; }

        public bool IsArchived { get; set; }

        public UserModel() { }

        public UserModel(UserDto dto)
        {
            Id = dto.Id;
            Email = dto.Email;
            FirstName = dto.FirstName;
            LastName = dto.LastName;
            Roles = dto.Roles;
            LastLoginDate = dto.LastLoginDate;
            IsActive = dto.IsActive;
            IsArchived = dto.IsArchived;
        }
    }
}

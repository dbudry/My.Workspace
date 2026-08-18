namespace My.Shared.Dtos.User
{
    public class UserDto
    {
        public string Id { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public List<string> Roles { get; set; } = new();
        public DateTimeOffset? LastLoginDate { get; set; }
        public bool IsActive { get; set; }
        public bool IsArchived { get; set; }
    }
}

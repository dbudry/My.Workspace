namespace My.Shared.Dtos
{
    public class ApplicationUserDto
    {

        public string FirstName { get; set; } = null!;

        public string LastName { get; set; } = null!;

        public string Username { get; set; } = null!;

        public bool IsActive { get; set; }

        public bool IsArchived { get; set; }
    }
}

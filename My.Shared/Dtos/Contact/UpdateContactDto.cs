namespace My.Shared.Dtos.Contact
{
    public class UpdateContactDto
    {
        public string ContactId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Title { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? ContactType { get; set; }
    }
}

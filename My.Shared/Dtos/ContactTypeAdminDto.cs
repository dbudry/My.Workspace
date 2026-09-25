namespace My.Shared.Dtos;

/// <summary>A person who can add a contact type. Name and email only.</summary>
public class ContactTypeAdminDto
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

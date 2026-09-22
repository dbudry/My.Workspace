using System.ComponentModel.DataAnnotations;

namespace My.DAL.Models;

/// <summary>
/// Singleton Google Drive owner for App Shared Drive.
/// Not an employee UserSettings token.
/// </summary>
public class AppDriveCredential
{
    [MaxLength(32)]
    public string AppDriveCredentialId { get; set; } = "default";

    public string? EncryptedRefreshToken { get; set; }

    [MaxLength(256)]
    public string? Email { get; set; }

    [MaxLength(128)]
    public string? SharedDriveId { get; set; }

    public DateTime? ConnectedAt { get; set; }

    [MaxLength(450)]
    public string? ConnectedByUserId { get; set; }
}

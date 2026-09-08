namespace My.Shared.Rules;

/// <summary>
/// Scopes for Intranet Drive (browse/attach in the company folder). Requested
/// separately from Calendar Connect so Settings → Connect does not send
/// restricted <c>drive.readonly</c> with Calendar (that combination 500s Google's
/// consent page for some Workspace users).
/// </summary>
public static class GoogleDriveOAuthRules
{
    public const string DriveFileScope = "https://www.googleapis.com/auth/drive.file";
    public const string DriveReadonlyScope = "https://www.googleapis.com/auth/drive.readonly";

    public const string ConsentRequiredMessage =
        "Google Drive is not connected. Connect Drive to browse or attach files.";

    public static readonly string[] ConnectScopes =
    [
        DriveFileScope,
        DriveReadonlyScope,
        GoogleCalendarOAuthRules.EmailScope,
        GoogleCalendarOAuthRules.OpenIdScope
    ];

    public static bool NeedsDriveConsent(bool hasRefreshToken, bool driveGranted) =>
        !hasRefreshToken || !driveGranted;

    public static bool IsConsentRequiredMessage(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && message.Contains("Google Drive is not connected", StringComparison.OrdinalIgnoreCase);
}
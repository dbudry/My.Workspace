namespace My.Shared.Rules;

/// <summary>
/// Admin Connect for the App Shared Drive. Full Drive scope is required
/// to create a Shared Drive and manage folders employees are not members of.
/// Stored as a singleton app credential — not on UserSettings.
/// </summary>
public static class GoogleAppDriveOAuthRules
{
    public const string DriveScope = "https://www.googleapis.com/auth/drive";

    public static readonly string[] ConnectScopes =
    [
        DriveScope,
        GoogleCalendarOAuthRules.EmailScope,
        GoogleCalendarOAuthRules.OpenIdScope
    ];

    /// <summary>
    /// Refresh must not send Intranet <c>drive.file</c> scopes or Google downscopes
    /// the Shared Drive grant. Empty = keep the original Connect grant.
    /// </summary>
    public static readonly string[] TokenRefreshScopes = [];
}

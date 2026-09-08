namespace My.Shared.Rules;

/// <summary>
/// Distinguishes Calendar vs Drive consent when Google returns to /settings?code=.
/// The SPA stores this in localStorage before leaving for accounts.google.com.
/// </summary>
public static class GoogleOAuthConnectKindRules
{
    public const string LocalStorageKey = "googleConnectKind";
    public const string Calendar = "calendar";
    public const string Drive = "drive";

    public static bool IsDrive(string? kind) =>
        string.Equals(kind, Drive, StringComparison.OrdinalIgnoreCase);
}
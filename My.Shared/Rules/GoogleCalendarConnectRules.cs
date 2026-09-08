namespace My.Shared.Rules;

/// <summary>
/// When the dashboard may start Google Calendar OAuth as part of sign-in.
/// First-time users (never connected) still auto-connect. After an explicit
/// Disconnect, login must not bounce them through Calendar consent again.
/// </summary>
public static class GoogleCalendarConnectRules
{
    public static bool ShouldAutoConnectOnLogin(bool isCalendarConnected, bool autoConnectOptOut) =>
        !isCalendarConnected && !autoConnectOptOut;

    /// <summary>
    /// Google redirects to /settings with error= when consent fails (no code).
    /// </summary>
    public static string? ExplainOauthRedirectError(string? error, string? description)
    {
        if (string.IsNullOrWhiteSpace(error))
            return null;

        if (error.Equals("access_denied", StringComparison.OrdinalIgnoreCase))
            return "Google Calendar was not connected because consent was declined.";

        var desc = description ?? "";
        if (error.Equals("server_error", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("backend", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("Backend Error", StringComparison.OrdinalIgnoreCase))
        {
            return "Google could not finish Calendar consent. Wait a few seconds and connect again from Settings.";
        }

        return "Google Calendar was not connected. Wait a few seconds and try Connect again from Settings.";
    }
}

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
}

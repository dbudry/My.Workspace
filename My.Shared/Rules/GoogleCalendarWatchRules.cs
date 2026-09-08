namespace My.Shared.Rules;

/// <summary>
/// Live Google Calendar import uses a push watch. Connected (token + calendar id)
/// is independent: an expired watch must not look like a disconnect.
/// </summary>
public static class GoogleCalendarWatchRules
{
    public static bool IsLiveWatchActive(string? channelId, DateTime? expiresAtUtc, DateTime utcNow) =>
        !string.IsNullOrWhiteSpace(channelId)
        && expiresAtUtc is { } expires
        && expires > utcNow;
}

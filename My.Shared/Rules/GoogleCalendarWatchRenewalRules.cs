namespace My.Shared.Rules;

/// <summary>
/// When a Google Calendar push channel should be re-registered so live import
/// does not die after Google's ~7-day watch expiry.
/// </summary>
public static class GoogleCalendarWatchRenewalRules
{
    /// <summary>
    /// Wider than the daily timer so one missed run still leaves three chances
    /// before Google drops the channel.
    /// </summary>
    public static readonly TimeSpan RenewWindow = TimeSpan.FromHours(96);

    /// <summary>
    /// No expiry yet (connect never registered a watch) or expiry within the window,
    /// including already expired.
    /// </summary>
    public static bool NeedsRenewal(DateTime? expiresAtUtc, DateTime utcNow) =>
        expiresAtUtc == null || expiresAtUtc.Value < utcNow.Add(RenewWindow);
}

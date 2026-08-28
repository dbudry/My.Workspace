using ConstantsClass = My.Shared.Constants.Constants;

namespace My.Shared.Rules;

public static class GoogleCalendarWebhookRules
{
    public const string SyncResourceState = "sync";
    public const string ExistsResourceState = "exists";

    /// <summary>
    /// Validates the channel token Google sends on push notifications.
    /// </summary>
    public static bool IsChannelTokenValid(string? incomingToken, string? storedToken) =>
        !string.IsNullOrEmpty(incomingToken)
        && !string.IsNullOrEmpty(storedToken)
        && string.Equals(incomingToken.Trim(), storedToken.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// Whether this notification should trigger an incremental import.
    /// Handshake ("sync"), disabled import, or missing Google credentials are no-ops.
    /// </summary>
    public static bool ShouldImport(
        string? resourceState,
        bool importEnabled,
        string? refreshToken,
        string? calendarId) =>
        string.Equals(resourceState, ExistsResourceState, StringComparison.OrdinalIgnoreCase)
        && importEnabled
        && !string.IsNullOrEmpty(refreshToken)
        && !string.IsNullOrEmpty(calendarId);

    /// <summary>
    /// Whether the HTTP webhook should enqueue an import. Handshake ("sync")
    /// and missing channel id are no-ops. Lookup and SQL happen on the queue
    /// trigger, not on Google's HTTP call.
    /// </summary>
    public static bool ShouldEnqueue(string? channelId, string? resourceState) =>
        !string.IsNullOrWhiteSpace(channelId)
        && string.Equals(resourceState, ExistsResourceState, StringComparison.OrdinalIgnoreCase);

    public static bool IsProbeChannel(string? channelId) =>
        string.Equals(channelId, ConstantsClass.API.GoogleCalendar.ProbeChannelId, StringComparison.Ordinal);

    /// <summary>
    /// Handshake / skip still return 200. Enqueue failure is a 500 so Google
    /// retries (Storage only — not SQL). Import retries are the queue trigger.
    /// </summary>
    public static bool AcknowledgeEvenIfImportFails => true;

    /// <summary>
    /// Google returns 410 Gone and no next sync token when the stored token is
    /// invalid. The caller must drop the token and list again without it, or
    /// every later webhook imports nothing.
    /// </summary>
    public static bool ShouldResyncWithoutToken(bool hadSyncToken, string? nextSyncToken) =>
        hadSyncToken && string.IsNullOrEmpty(nextSyncToken);

    /// <summary>
    /// Log Error from this dequeue count (inclusive) up to host.json
    /// <c>queues.maxDequeueCount</c> (8). After that the message is poison.
    /// </summary>
    public const int PoisonDequeueWarningAt = 5;

    public static bool IsApproachingPoison(int dequeueCount) =>
        dequeueCount >= PoisonDequeueWarningAt;
}

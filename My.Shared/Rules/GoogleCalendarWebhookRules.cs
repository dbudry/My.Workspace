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
    /// Google retries any non-2xx. Import failures must still acknowledge so a
    /// timeout or SQL blip cannot storm the function (and the SQL pool).
    /// The nightly self-heal covers missed deltas.
    /// </summary>
    public static bool AcknowledgeEvenIfImportFails => true;
}

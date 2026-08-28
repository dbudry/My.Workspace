namespace My.Shared.Rules;

/// <summary>
/// Plain-language status for Admin → Debug → Google Calendar. No Azure types.
/// </summary>
public static class GoogleCalendarSyncRules
{
    public const string TopicCalendar = "calendar";

    public const string StatusReady = "Ready";
    public const string StatusImportOff = "Import off";
    public const string StatusWatchExpired = "Watch expired";
    public const string StatusNoWatch = "No push watch";

    public static string UserStatus(bool importEnabled, string? channelId, DateTime? watchExpiresAtUtc, DateTime utcNow)
    {
        if (!importEnabled)
            return StatusImportOff;
        if (string.IsNullOrEmpty(channelId))
            return StatusNoWatch;
        if (watchExpiresAtUtc != null && watchExpiresAtUtc.Value <= utcNow)
            return StatusWatchExpired;
        return StatusReady;
    }

    public static bool IsCalendarLogTopic(string? topic) =>
        string.Equals(topic?.Trim(), TopicCalendar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maps a raw Function log line to a short explanation for the admin table.
    /// Unknown lines pass through unchanged.
    /// </summary>
    public static string ExplainTrace(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Calendar import log.";

        if (Contains(message, "approaching the poison"))
            return "The import queue failed several times. The next failure drops the message. Use Pull missed events for that person.";
        if (Contains(message, "LeaseIdMissing") || Contains(message, "lease on the blob"))
            return "Two imports ran at once for the same person. The next queue retry should finish; if not, Pull missed events.";
        if (Contains(message, "cold start"))
            return "Storage timed out while the Function App was waking up. Try Test import queue again.";
        if (Contains(message, "Failed to enqueue") || Contains(message, "failed to enqueue"))
            return "Could not write to the import queue (storage). Tagged events will not import until this succeeds.";
        if (Contains(message, "missing ChannelId"))
            return "A queue message could not be read. The worker dropped it.";
        if (Contains(message, "unknown channel"))
            return "Google pushed a watch we no longer have (often after nightly watch renewal). Nightly or Pull missed events still import.";
        if (Contains(message, "invalid channel token"))
            return "Google push token did not match. The watch may have been renewed. Nightly or Pull missed events still import.";
        if (Contains(message, "queue worker is running"))
            return "The import worker picked up the test message. The queue is running.";
        if (Contains(message, "admin probe enqueued"))
            return "Test import queue wrote a message. Wait a few seconds and Refresh.";
        if (Contains(message, "watch renewal skipped"))
            return "Watches were not renewed — no webhook URL. Live import will die after about a week.";
        if (Contains(message, "watch renewed"))
            return "A Google Calendar watch was re-registered. Tagged events can import live again.";
        if (Contains(message, "no channels due"))
            return "Watch renewal ran; every connected account was already inside the window.";
        if (Contains(message, "could not be renewed"))
            return "A watch failed to re-register. That person's live import is still down.";
        if (Contains(message, "sync token invalidated"))
            return "Google reset the incremental sync token. Tyme re-listed recent events instead of skipping.";
        if (Contains(message, "Failed to import Google event"))
            return "One calendar event failed to save. Others in that batch still ran.";
        if (Contains(message, "import skipped"))
            return "Import did not run for this user (import off, or Google is not fully connected).";
        if (Contains(message, "import finished"))
            return "Import finished. Counts are in the log line.";
        if (Contains(message, "import started"))
            return "Import started after SQL was available.";
        if (Contains(message, "Enqueued Google calendar import"))
            return "Google's push was accepted and written to the import queue.";
        if (Contains(message, "webhook handshake"))
            return "Google watch handshake. No import (expected).";
        if (Contains(message, "webhook received"))
            return "Google called the webhook.";

        return message.Trim();
    }

    private static bool Contains(string message, string fragment) =>
        message.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}

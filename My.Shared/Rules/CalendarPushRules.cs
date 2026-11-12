namespace My.Shared.Rules;

/// <summary>
/// Rules for pushing a Tyme edit back onto the user's primary Google calendar.
/// Pure functions — the caller reads the event fields from Google and passes them in.
/// </summary>
public static class CalendarPushRules
{
    /// <summary>
    /// Whether Tyme may write a Tyme edit (title, start, end, color) onto this event.
    ///
    /// <para>
    /// Only the organizer can change an event's times. For an invite the user only
    /// attends, Google rejects or ignores the write, so Tyme does not try: the edit
    /// stays in Tyme, and inbound sync keeps it until the organizer moves the meeting
    /// (see <see cref="CalendarImportRules.GoogleTimesMoved"/>).
    /// </para>
    ///
    /// <para>
    /// <paramref name="organizerSelf"/> is Google's <c>organizer.self</c>. Null means
    /// Google sent no organizer, which cannot prove the user is a guest, so the push
    /// goes ahead (same as before this rule existed).
    /// </para>
    /// </summary>
    public static bool CanPushUpdate(bool? organizerSelf) => organizerSelf != false;

    /// <summary>
    /// A Get of a deleted event can return 200 with <c>status=cancelled</c>. Writing
    /// to it would bring the event back, so callers treat it like a 404.
    /// </summary>
    public static bool IsCancelled(string? status) =>
        string.Equals(status, "cancelled", System.StringComparison.OrdinalIgnoreCase);
}

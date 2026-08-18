namespace My.Shared.Rules;

/// <summary>
/// Rules that gate inbound Google Calendar event import into Tyme. Pure functions —
/// no DB access, no Google API access. The caller is responsible for looking up
/// the relevant event fields and passing them in.
///
/// Why a helper: the inbound sync loop in GoogleCalendarFunction is otherwise
/// untestable (it's wired to the Google SDK + DB repositories). Extracting the
/// decision points means the rules can be pinned by tests independent of the
/// infrastructure they run inside.
/// </summary>
public static class CalendarImportRules
{
    /// <summary>
    /// Decides whether a calendar event should be imported based on the calendar
    /// owner's invite-response status.
    ///
    /// Google marks the owner's attendee row with <c>Self=true</c>; the caller
    /// passes that row's <c>responseStatus</c>, or <c>null</c> if the owner is
    /// not in the attendees list (self-organized events, or events with no
    /// attendees). Pass <paramref name="isOrganizer"/> when
    /// <c>event.Organizer.Self</c> (or Creator.Self) is true.
    ///
    /// <para>
    /// Returns <see cref="InviteImportDecision.Skip"/> only when the user is an
    /// attendee and has explicitly <c>declined</c>. Every other case — the user
    /// organized the event, is not an attendee (organizer-only / no-invite event),
    /// has <c>accepted</c>, has marked <c>tentative</c>, is still <c>needsAction</c>
    /// (no response yet), or has an empty/null status — returns
    /// <see cref="InviteImportDecision.Import"/>. This is intentionally permissive:
    /// Google often still lists the organizer as <c>needsAction</c> on their own
    /// attendee row, and a not-yet-responded invite the user tagged with [slug] is
    /// still an opt-in.
    /// </para>
    /// </summary>
    public static InviteImportDecision EvaluateInvite(string? selfResponseStatus, bool isOrganizer = false)
    {
        // This helper runs only after a [slug] matched. The tag is the user's
        // opt-in. Skip only an explicit decline — needsAction used to drop
        // "[admin] Company Meeting" that the organizer had not clicked Accept on.
        if (isOrganizer)
            return InviteImportDecision.Import;

        if (string.Equals(selfResponseStatus, "declined", System.StringComparison.OrdinalIgnoreCase))
            return InviteImportDecision.Skip;

        return InviteImportDecision.Import;
    }

    /// <summary>
    /// Whether a Google <c>cancelled</c> event should delete the matching Tyme row.
    /// True only for incremental webhook sync (we already had a sync token).
    /// Initial connect, reconnect, pull-missed, and nightly range scans must not
    /// delete — those lists include tombstones from "I disconnected and cleaned
    /// Google," which is not "delete my Tyme week."
    /// </summary>
    public static bool ShouldDeleteTrackedTaskOnGoogleCancel(bool incrementalSync) => incrementalSync;

    /// <summary>
    /// Distinguishes a genuine edit made directly in Google Calendar from the
    /// webhook echo of Tyme's own push, for events tagged <c>source=tyme</c>.
    ///
    /// <para>
    /// Before this existed, any inbound event carrying <c>source=tyme</c> was
    /// unconditionally skipped as "our own echo" — including edits the user made
    /// by hand in Google after Tyme created the event. Those edits were silently
    /// discarded because nothing ever compared Google's timestamp to when Tyme
    /// last wrote the event.
    /// </para>
    ///
    /// <para>
    /// <paramref name="lastPushedUtc"/> is <c>TrackedTask.GoogleEventUpdatedUtc</c>
    /// — the <c>updated</c> timestamp Google returned the last time Tyme itself
    /// created or updated this event. <paramref name="googleUpdatedUtc"/> is the
    /// <c>updated</c> timestamp on the event as currently polled/pushed from
    /// Google. A small tolerance absorbs clock skew and the gap between our push
    /// completing and the next poll picking up that same write as "new" — without
    /// it, our own successful push would immediately look like a foreign edit and
    /// re-trigger an import.
    /// </para>
    ///
    /// <para>
    /// Missing data always counts as genuine: no baseline to compare against
    /// (<paramref name="lastPushedUtc"/> null — pre-migration rows, or a push that
    /// predates this feature) or no timestamp on the incoming event
    /// (<paramref name="googleUpdatedUtc"/> null) means we can't prove it's an
    /// echo, so we let it through rather than risk swallowing a real edit.
    /// </para>
    /// </summary>
    public static bool IsGenuineExternalEdit(
        System.DateTime? googleUpdatedUtc, System.DateTime? lastPushedUtc, double toleranceSeconds = 5.0)
    {
        if (googleUpdatedUtc == null || lastPushedUtc == null)
            return true;

        return googleUpdatedUtc.Value > lastPushedUtc.Value.AddSeconds(toleranceSeconds);
    }

    /// <summary>
    /// Outcome of the invite rule. <see cref="Skip"/> additionally implies that
    /// if a previously-imported entry exists for this event, the caller should
    /// remove it (gated on month-not-submitted so we don't rewrite billable history).
    /// </summary>
    public enum InviteImportDecision
    {
        /// <summary>Event should be imported as a tracked task.</summary>
        Import,
        /// <summary>Event should be skipped; if previously imported, remove it (gated on month-not-submitted).</summary>
        Skip,
    }
}

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
    /// attendees).
    ///
    /// <para>
    /// Returns <see cref="InviteImportDecision.Import"/> when:
    /// <list type="bullet">
    ///   <item>the user is not an attendee (organizer-only / no-invite event), or</item>
    ///   <item>the user is an attendee and has <c>accepted</c>, or</item>
    ///   <item>the user is an attendee and has marked <c>tentative</c> (treated as
    ///         accepted so users aren't penalized for honest uncertainty).</item>
    /// </list>
    /// Returns <see cref="InviteImportDecision.Skip"/> for any other status —
    /// declined, needsAction (no response yet), empty/null on an existing
    /// attendee row, or unknown future values Google might add.
    /// </para>
    /// </summary>
    public static InviteImportDecision EvaluateInvite(string? selfResponseStatus)
    {
        if (selfResponseStatus is null)
            return InviteImportDecision.Import;

        return string.Equals(selfResponseStatus, "accepted", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(selfResponseStatus, "tentative", System.StringComparison.OrdinalIgnoreCase)
                ? InviteImportDecision.Import
                : InviteImportDecision.Skip;
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

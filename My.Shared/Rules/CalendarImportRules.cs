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
    /// Whether a Google <c>cancelled</c>/declined event should delete the matching Tyme row.
    /// Requires incremental webhook sync (we already had a sync token) — initial connect,
    /// reconnect, pull-missed, and nightly range scans must not delete, since those lists
    /// include tombstones from "I disconnected and cleaned Google," which is not "delete my
    /// Tyme week." Also requires the linked task's project to still be eligible for personal
    /// sync — once Availability-only excludes a project, a leftover event for it disappearing
    /// on Google must not touch the Tyme row, since that project "stays in Tyme" by design.
    /// </summary>
    public static bool ShouldDeleteTrackedTaskOnGoogleCancel(bool incrementalSync, bool stillEligibleForPersonalSync) =>
        incrementalSync && stillEligibleForPersonalSync;

    /// <summary>
    /// How far ahead of <c>utcNow</c> a tagged Google event may be imported as Tyme time.
    /// Matches the first-sync list window. Incremental sync is otherwise unbounded, so a
    /// weekly <c>[slug]</c> series with no end date would otherwise create a task per week
    /// for decades (Submit filled through 2040).
    /// </summary>
    public static readonly TimeSpan ImportLookahead = TimeSpan.FromDays(90);

    /// <summary>
    /// Tagged events whose start is after now + <see cref="ImportLookahead"/> are not
    /// imported (and not updated). Past and near-future events still import.
    /// </summary>
    public static bool ShouldImportByStart(System.DateTime startUtc, System.DateTime utcNow) =>
        startUtc <= utcNow + ImportLookahead;

    /// <summary>
    /// Google instance ids are <c>{seriesId}_{yyyyMMdd}T{HHmmss}Z</c>. Cancelling the
    /// series often sends the master id (or <c>recurringEventId</c>) without the suffix.
    /// Exact match still covers a single-instance cancel.
    /// </summary>
    public static bool GoogleEventIdMatchesCancel(
        string? taskGoogleEventId, string? cancelledEventId, string? recurringEventId)
    {
        if (string.IsNullOrEmpty(taskGoogleEventId))
            return false;
        if (IdsEqualOrInstanceOf(taskGoogleEventId, cancelledEventId))
            return true;
        if (IdsEqualOrInstanceOf(taskGoogleEventId, recurringEventId))
            return true;
        return false;
    }

    /// <summary>
    /// Same calendar occurrence: all-day by date, timed within 2 minutes.
    /// Used to relink a recurring instance whose Google id changed after an organizer update.
    /// </summary>
    public static bool IsSameOccurrence(System.DateTime a, System.DateTime b, bool allDay)
    {
        if (allDay)
            return a.Date == b.Date;
        return System.Math.Abs((a - b).TotalMinutes) < 2;
    }

    /// <summary>
    /// Tyme-exported events carry <c>source=tyme</c>. If no TrackedTask is linked yet,
    /// importing them would clone our own export (invite → Tyme → new Google copy → Tyme again).
    /// </summary>
    public static bool ShouldSkipUnlinkedTymeExport(bool isTymeSourced, bool existingFound) =>
        isTymeSourced && !existingFound;

    /// <summary>
    /// Unlinked leftover from a cancelled series instance vs a brand-new tagged event.
    /// Same project + time is not enough (two legitimate entries that day). Require a
    /// recurring Google instance and the same display name (slug already stripped).
    /// </summary>
    public static bool CanRelinkUnlinkedSeriesInstance(
        bool isRecurringInstance, string? unlinkedGoogleEventId, string? taskName, string? googleCleanName) =>
        isRecurringInstance
        && string.IsNullOrEmpty(unlinkedGoogleEventId)
        && NamesMatchForRelink(taskName, googleCleanName);

    public static bool NamesMatchForRelink(string? taskName, string? googleCleanName)
    {
        static string Norm(string? s) => (s ?? "").Trim();
        var a = Norm(taskName);
        var b = Norm(googleCleanName);
        return a.Length > 0 && b.Length > 0
            && string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IdsEqualOrInstanceOf(string taskGoogleEventId, string? seriesOrInstanceId)
    {
        if (string.IsNullOrEmpty(seriesOrInstanceId))
            return false;
        if (string.Equals(taskGoogleEventId, seriesOrInstanceId, System.StringComparison.Ordinal))
            return true;
        return taskGoogleEventId.StartsWith(seriesOrInstanceId + "_", System.StringComparison.Ordinal);
    }

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

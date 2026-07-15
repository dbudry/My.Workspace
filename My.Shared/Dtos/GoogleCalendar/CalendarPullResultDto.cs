namespace My.Shared.Dtos.GoogleCalendar
{
    /// <summary>
    /// Returned by POST /api/googlecalendar/pullfromgoogle. Reports the per-outcome
    /// counts for a Google → Tyme resync over a date range — what got created,
    /// updated, and why anything was skipped.
    ///
    /// Self-service: a user can run this against their own calendar to recover from
    /// a missed webhook (channel expired, transient error, etc). Global Admin can
    /// run it against any user by passing <c>?userId=</c>.
    /// </summary>
    public class CalendarPullResultDto
    {
        /// <summary>New TrackedTasks created from matched-slug Google events.</summary>
        public int Created { get; set; }

        /// <summary>Existing TrackedTasks whose Google event had been edited since import.</summary>
        public int Updated { get; set; }

        /// <summary>Google cancellations that propagated into a TrackedTask delete.</summary>
        public int Cancelled { get; set; }

        /// <summary>
        /// Events Tyme itself pushed (extended property <c>source=tyme</c>) — round-trip
        /// echoes, deliberately not re-imported.
        /// </summary>
        public int SkippedOurs { get; set; }

        /// <summary>Events with no usable start/end shape — neither timed datetime nor all-day date pair.</summary>
        public int SkippedNoDates { get; set; }

        /// <summary>Events with no <c>[slug]</c> tag — by design these stay private to the user's calendar.</summary>
        public int SkippedNoTag { get; set; }

        /// <summary>Events with a <c>[slug]</c> tag that didn't match any active project.</summary>
        public int SkippedUnresolvedTag { get; set; }

        /// <summary>Events the user hasn't accepted (response status declined / not yet).</summary>
        public int SkippedDeclinedInvite { get; set; }

        /// <summary>Events whose TrackedTask falls in an already-submitted month — left untouched.</summary>
        public int SkippedMonthSubmitted { get; set; }

        /// <summary>Per-event Google API failures during the scan. Counts log lines, doesn't fail the request.</summary>
        public int Failed { get; set; }

        /// <summary>Total events Google returned for the range. <c>Created + Updated + Cancelled + Skipped*</c> = Scanned - Failed.</summary>
        public int Scanned { get; set; }

        /// <summary>Top-level error (auth, calendar not connected, etc). Per-event failures go in <see cref="Failed"/>.</summary>
        public string? Error { get; set; }
    }
}

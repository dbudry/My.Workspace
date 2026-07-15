namespace My.DAL.Models
{
    public class UserSettings
    {
        public string UserSettingsId { get; set; } = null!;

        public string UserId { get; set; } = null!;

        public bool Use24HourTime { get; set; }

        /// <summary>IANA timezone ID (e.g. "America/New_York"). Null = not yet set.</summary>
        public string? TimeZone { get; set; }

        /// <summary>Encrypted Google OAuth refresh token. Null = not connected.</summary>
        public string? GoogleRefreshToken { get; set; }

        /// <summary>The calendarId of the user's "Tyme" sub-calendar in Google.</summary>
        public string? GoogleCalendarId { get; set; }

        /// <summary>The Google account email the user connected with (display only).</summary>
        public string? GoogleCalendarEmail { get; set; }

        /// <summary>If true, Tyme tasks are pushed to the user's primary Google calendar.</summary>
        public bool PublishToGoogleCalendar { get; set; } = true;

        /// <summary>If true, slug-tagged events on the user's primary Google calendar are imported as Tyme tasks.</summary>
        public bool ImportFromGoogleCalendar { get; set; } = true;

        /// <summary>Our UUID that identifies this user's push channel on Google's side.</summary>
        public string? GoogleChannelId { get; set; }

        /// <summary>
        /// Secret echoed by Google as <c>X-Goog-Channel-Token</c> on webhook calls.
        /// Stored separately from <see cref="GoogleChannelId"/> so channel-id lookup alone is not enough.
        /// </summary>
        public string? GoogleChannelToken { get; set; }

        /// <summary>Google's resource id for the active watch; needed to stop the channel.</summary>
        public string? GoogleResourceId { get; set; }

        /// <summary>UTC expiry of the push channel. Google caps at ~1 week; we renew before then.</summary>
        public DateTime? GoogleChannelExpiresAt { get; set; }

        /// <summary>Opaque token used for incremental event sync.</summary>
        public string? GoogleSyncToken { get; set; }

        /// <summary>
        /// Google Calendar event color id ("1"-"11") applied to outbound Tyme events whose
        /// project slug resolves cleanly. Null = use the calendar's default color.
        /// </summary>
        public string? TymeEventColorId { get; set; }

        /// <summary>
        /// Google Calendar event color id applied to outbound Tyme events without a resolvable
        /// project slug — flagged so the user can spot "needs a project" entries at a glance.
        /// Defaults to "11" (Tomato red) for new users.
        /// </summary>
        public string? TymeUnmatchedEventColorId { get; set; } = "11";

        /// <summary>
        /// Which color a project picks up in lists, charts, and the calendar — the project's
        /// group color (with org fallback), the org color, the group color only, or none.
        /// Default = GroupThenOrganization (0), matching ProjectManager's historical behavior.
        /// </summary>
        public int ProjectColorSource { get; set; }

        /// <summary>
        /// JSON-serialized array of IntranetPageId strings that this (Intranet-scoped) user
        /// has favorited / bookmarked. Rendered as quick-access links on their personal dashboard.
        /// </summary>
        public string? FavoriteIntranetPageIdsJson { get; set; }

        /// <summary>
        /// Set when the user completes or skips the one-time calendar backfill prompt after
        /// connecting Google. Prevents re-prompting on reconnect or a new browser session.
        /// </summary>
        public DateTime? CalendarBackfillAcknowledgedUtc { get; set; }

        public ApplicationUser User { get; set; } = null!;
    }
}

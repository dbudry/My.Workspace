using My.Shared.Rules;

namespace My.Shared.Dtos.UserSettings
{
    public class UserSettingsDto
    {
        public string UserSettingsId { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public bool Use24HourTime { get; set; }
        /// <summary>IANA timezone ID (e.g. "America/New_York"). Null = not yet set.</summary>
        public string? TimeZone { get; set; }

        /// <summary>True if the user has connected a Google account and Tyme is syncing with their primary calendar.</summary>
        public bool IsGoogleCalendarConnected { get; set; }
        /// <summary>The Google account email the user connected with (display only).</summary>
        public string? GoogleCalendarEmail { get; set; }
        public bool PublishToGoogleCalendar { get; set; }
        public bool ImportFromGoogleCalendar { get; set; }

        /// <summary>Google color id ("1"-"11") for matched (slug-tagged) Tyme events. Null = calendar default.</summary>
        public string? TymeEventColorId { get; set; }
        /// <summary>Google color id ("1"-"11") for unmatched Tyme events. Null = calendar default (not recommended).</summary>
        public string? TymeUnmatchedEventColorId { get; set; }

        /// <summary>How project rows/chart segments pick up a color across the app.</summary>
        public ProjectColorSource ProjectColorSource { get; set; }

        /// <summary>
        /// PageIds of Intranet pages this user has favorited. Only meaningful for users
        /// with at least User:Intranet. Rendered on the personal dashboard for quick access.
        /// </summary>
        public List<string> FavoriteIntranetPageIds { get; set; } = new();

        /// <summary>True after the user has seen and answered the post-connect calendar backfill prompt.</summary>
        public bool CalendarBackfillPromptAcknowledged { get; set; }
    }
}

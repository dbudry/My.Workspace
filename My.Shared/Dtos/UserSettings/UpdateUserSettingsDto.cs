using My.Shared.Rules;

namespace My.Shared.Dtos.UserSettings
{
    public class UpdateUserSettingsDto
    {
        public bool Use24HourTime { get; set; }
        /// <summary>IANA timezone ID (e.g. "America/New_York"). Null = not yet set.</summary>
        public string? TimeZone { get; set; }

        public bool PublishToGoogleCalendar { get; set; }
        public bool ImportFromGoogleCalendar { get; set; }

        /// <summary>Google color id ("1"-"11") for matched Tyme events. Null = calendar default.</summary>
        public string? TymeEventColorId { get; set; }
        /// <summary>Google color id ("1"-"11") for unmatched Tyme events. Null = calendar default.</summary>
        public string? TymeUnmatchedEventColorId { get; set; }

        /// <summary>How project rows/chart segments pick up a color across the app.</summary>
        public ProjectColorSource ProjectColorSource { get; set; }

        /// <summary>
        /// PageIds the user wants to favorite on their Intranet dashboard.
        /// Sent as part of settings for convenience; dedicated favorite toggle endpoints
        /// also exist under the Intranet module.
        /// </summary>
        public List<string> FavoriteIntranetPageIds { get; set; } = new();
    }
}

namespace My.Shared.Dtos.GoogleCalendar
{
    /// <summary>
    /// POST googlecalendar/resume. Restarts the watch with the stored refresh token.
    /// Does not send the user to Google unless <see cref="NeedsConsent"/> is set.
    /// </summary>
    public class GoogleCalendarResumeResultDto
    {
        public bool Resumed { get; set; }

        /// <summary>No usable token. Client should open Google consent with prompt=consent.</summary>
        public bool NeedsConsent { get; set; }

        /// <summary>Shown when the watch could not start and consent will not help (localhost, Google 5xx).</summary>
        public string? Error { get; set; }
    }
}

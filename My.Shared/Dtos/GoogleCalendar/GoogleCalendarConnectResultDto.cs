namespace My.Shared.Dtos.GoogleCalendar
{
    public class GoogleCalendarConnectResultDto
    {
        public bool Connected { get; set; }
        public string? Email { get; set; }

        /// <summary>
        /// True when the server had to drop the previously-connected Google account's
        /// Drive grant to recover a broken Calendar connection. Intranet Drive needs to
        /// be reconnected separately.
        /// </summary>
        public bool DriveReconnectNeeded { get; set; }

        /// <summary>
        /// True when Google's watch/push-channel registration failed for the final token
        /// this callback ended up with. Settings were still saved, but inbound sync (Google
        /// → Tyme) will not run until the user reconnects again.
        /// </summary>
        public bool SyncNotStarted { get; set; }
    }
}

namespace My.Shared.Dtos.GoogleCalendar
{
    public class GoogleCalendarCallbackDto
    {
        public string? Code { get; set; }
        public string? RedirectUri { get; set; }

        /// <summary>
        /// True when this return is from a consent page already opened with
        /// <c>prompt=consent</c> after a rejected token. The server must not
        /// ask for that page again.
        /// </summary>
        public bool ConsentAlreadyForced { get; set; }
    }
}
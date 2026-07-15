namespace My.Shared.Dtos.GoogleCalendar
{
    /// <summary>
    /// Returned by POST /api/googlecalendar/backfill. Reports how many of the user's
    /// tracked tasks in the requested date range were pushed to their Tyme Google
    /// calendar, and which were skipped or failed.
    /// </summary>
    public class CalendarBackfillResultDto
    {
        /// <summary>Tasks that were eligible (no existing GoogleEventId) and successfully pushed.</summary>
        public int Created { get; set; }

        /// <summary>Tasks already had a GoogleEventId — already on the calendar, left alone.</summary>
        public int Skipped { get; set; }

        /// <summary>Tasks where the Google API call threw — logged server-side, surfaced to the user.</summary>
        public int Failed { get; set; }
    }
}

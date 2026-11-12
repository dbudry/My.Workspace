using Google.Apis.Calendar.v3.Data;

namespace My.Functions.Services
{
    /// <summary>What <see cref="GoogleCalendarService.UpdateEventAsync"/> did with a Tyme edit.</summary>
    public enum CalendarEventUpdateKind
    {
        /// <summary>Tyme's fields were written. <see cref="CalendarEventUpdateResult.Event"/> is Google's response.</summary>
        Pushed,

        /// <summary>
        /// The user does not organize this event, so Tyme did not write. The edit stays in
        /// Tyme only. <see cref="CalendarEventUpdateResult.Event"/> is the event as Google holds it.
        /// </summary>
        NotOrganizer,

        /// <summary>The event is cancelled on Google. Treat like a 404: drop the link and recreate.</summary>
        Gone,
    }

    public sealed record CalendarEventUpdateResult(CalendarEventUpdateKind Kind, Event Event);
}

namespace My.DAL.Models
{
    public class TrackedTask
    {
        public string TaskId { get; set; } = null!;
        /// <summary>
        /// Free-text Details ("What are you working on"). Optional — null when omitted.
        /// Application code (TrackedTaskFunctions Create/Update) normalizes blank input to
        /// null before saving. Readers that need a display-safe value should coalesce to ""
        /// (e.g. AppMapper.TrackedTaskToDto callers do this explicitly) rather than assume
        /// non-null here.
        /// </summary>
        public string? Details { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsBillable { get; set; }
        public string? ProjectId { get; set; }
        public Project? Project { get; set; }
        public string UserId { get; set; } = null!;
        public ApplicationUser User { get; set; } = null!;

        /// <summary>
        /// When set, this row is a session belonging to a stopwatch work item.
        /// Null for calendar/manual/imported entries.
        /// </summary>
        public string? StopwatchItemId { get; set; }
        public StopwatchItem? StopwatchItem { get; set; }

        /// <summary>
        /// Google Calendar event id if this task is mirrored on the user's primary Google calendar.
        /// Null = not synced (either not connected, or publish disabled).
        /// </summary>
        public string? GoogleEventId { get; set; }

        /// <summary>
        /// Google's <c>updated</c> timestamp (UTC) as of the last time Tyme pushed to or
        /// pulled from this event. Lets inbound sync (GoogleCalendarFunction.TryImportEventAsync)
        /// tell a genuine edit the user made in Google Calendar apart from the webhook echo
        /// of Tyme's own just-completed push — only an event whose Google <c>updated</c> is
        /// newer than this is treated as a real inbound change. Null for tasks never synced.
        /// </summary>
        public DateTime? GoogleEventUpdatedUtc { get; set; }

        /// <summary>
        /// When true, this entry covers full calendar days (Vacation/OOO style); StartDate
        /// is the first day at 00:00 in the user's zone and EndDate is the last day at 23:59.
        /// Duration is the elapsed work time. Timed entries stay under 24h and map to
        /// SQL <c>time</c> (so 13:17:30 is stored exactly). All-day totals of 24h or more
        /// are derived from StartDate/EndDate × workday hours at read time — <c>time</c>
        /// cannot store 24:00.
        /// </summary>
        public bool IsAllDay { get; set; }

        /// <summary>
        /// Sister Google Calendar event id on the workspace-wide Team Availability calendar.
        /// Only set when the task's project has <see cref="Project.IsSharedAvailability"/> = true
        /// and the workspace has configured a calendar id. Null otherwise.
        /// </summary>
        public string? TeamAvailabilityEventId { get; set; }
    }
}

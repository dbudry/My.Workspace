namespace My.Shared.Rules;

/// <summary>
/// Personal Google Calendar import/publish can be limited to shared-availability
/// projects (OOO, PTO). Team Availability sister events are not this setting —
/// they follow the project flag.
/// </summary>
public static class GoogleCalendarAvailabilitySyncRules
{
    public static bool AllowsPersonalSync(bool availabilityOnly, bool projectIsSharedAvailability) =>
        !availabilityOnly || projectIsSharedAvailability;

    public static bool ShouldPublishToPersonalCalendar(
        bool publishEnabled, bool availabilityOnly, bool projectIsSharedAvailability) =>
        publishEnabled && AllowsPersonalSync(availabilityOnly, projectIsSharedAvailability);

    /// <summary>
    /// Unlink/delete leftover personal events only when export is still on and
    /// Availability-only excludes this project. Export-off keeps the GoogleEventId
    /// so turning export back on does not duplicate the event.
    /// </summary>
    public static bool ShouldUnlinkPersonalEvent(
        bool publishEnabled, bool availabilityOnly, bool projectIsSharedAvailability) =>
        publishEnabled && !AllowsPersonalSync(availabilityOnly, projectIsSharedAvailability);
}

using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Pins the rules that stop inbound Google sync from overwriting a time the user
/// corrected in Tyme. Scenario that drove them: an attendee (not organizer) changed a
/// 30-minute meeting to 60 minutes in Tyme; the nightly self-heal re-imported the
/// invite and reset it to 30 minutes, because Tyme cannot move the organizer's event.
/// </summary>
public class CalendarSyncOverwriteRulesTests
{
    private static readonly DateTime Synced = new(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc);

    // --- IsUnchangedSinceLastSync ---

    [Fact]
    public void Same_updated_timestamp_is_unchanged()
    {
        Assert.True(CalendarImportRules.IsUnchangedSinceLastSync(Synced, Synced));
    }

    [Fact]
    public void Updated_within_tolerance_is_unchanged()
    {
        Assert.True(CalendarImportRules.IsUnchangedSinceLastSync(Synced.AddSeconds(3), Synced));
    }

    [Fact]
    public void Newer_updated_timestamp_is_changed()
    {
        Assert.False(CalendarImportRules.IsUnchangedSinceLastSync(Synced.AddMinutes(1), Synced));
    }

    [Fact]
    public void Missing_baseline_counts_as_changed()
    {
        Assert.False(CalendarImportRules.IsUnchangedSinceLastSync(Synced, null));
    }

    [Fact]
    public void Missing_google_timestamp_counts_as_changed()
    {
        Assert.False(CalendarImportRules.IsUnchangedSinceLastSync(null, Synced));
    }

    // --- GoogleTimesMoved ---

    private static readonly DateTime Start = new(2026, 11, 10, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End30 = Start.AddMinutes(30);

    [Fact]
    public void Same_times_did_not_move()
    {
        // Organizer edited the description: Google's updated bumps, times stay.
        Assert.False(CalendarImportRules.GoogleTimesMoved(
            Start, End30, lastSyncedAllDay: false, Start, End30, googleAllDay: false));
    }

    [Fact]
    public void Organizer_extending_the_meeting_moved()
    {
        Assert.True(CalendarImportRules.GoogleTimesMoved(
            Start, End30, false, Start, Start.AddHours(1), false));
    }

    [Fact]
    public void Organizer_moving_the_start_moved()
    {
        Assert.True(CalendarImportRules.GoogleTimesMoved(
            Start, End30, false, Start.AddHours(2), End30.AddHours(2), false));
    }

    [Fact]
    public void One_minute_move_counts()
    {
        Assert.True(CalendarImportRules.GoogleTimesMoved(
            Start, End30, false, Start.AddMinutes(1), End30, false));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Missing_snapshot_counts_as_moved(bool startMissing, bool endMissing)
    {
        Assert.True(CalendarImportRules.GoogleTimesMoved(
            startMissing ? null : Start, endMissing ? null : End30, false, Start, End30, false));
    }

    [Fact]
    public void Switch_between_all_day_and_timed_moved()
    {
        Assert.True(CalendarImportRules.GoogleTimesMoved(
            Start.Date, Start.Date.AddHours(23).AddMinutes(59), lastSyncedAllDay: true,
            Start.Date, Start.Date.AddHours(23).AddMinutes(59), googleAllDay: false));
    }

    [Fact]
    public void All_day_compares_by_date()
    {
        var day = new DateTime(2026, 11, 10);
        Assert.False(CalendarImportRules.GoogleTimesMoved(
            day, day.AddHours(23).AddMinutes(59), true, day, day.AddHours(23), true));
        Assert.True(CalendarImportRules.GoogleTimesMoved(
            day, day.AddHours(23).AddMinutes(59), true, day, day.AddDays(1).AddHours(23), true));
    }

    // --- CalendarPushRules ---

    [Fact]
    public void Organizer_can_push()
    {
        Assert.True(CalendarPushRules.CanPushUpdate(organizerSelf: true));
    }

    [Fact]
    public void Attendee_cannot_push()
    {
        Assert.False(CalendarPushRules.CanPushUpdate(organizerSelf: false));
    }

    [Fact]
    public void Unknown_organizer_pushes()
    {
        Assert.True(CalendarPushRules.CanPushUpdate(organizerSelf: null));
    }

    [Theory]
    [InlineData("cancelled", true)]
    [InlineData("CANCELLED", true)]
    [InlineData("confirmed", false)]
    [InlineData("tentative", false)]
    [InlineData(null, false)]
    public void Cancelled_status_is_detected(string? status, bool expected)
    {
        Assert.Equal(expected, CalendarPushRules.IsCancelled(status));
    }
}

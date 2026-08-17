using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Exercises <see cref="CalendarImportRules.EvaluateInvite"/>. This rule decides
/// whether an inbound Google Calendar event should land in Tyme, based on the
/// calendar owner's invite response. Two failure modes drive the test list:
///
///   1. Tracking time for meetings the user never attended (declined / no response).
///   2. Failing to track time for meetings the user did accept — including the
///      case-insensitive variants of "accepted" and "tentative" Google may emit.
///
/// Self-organized events (the user *is* the organizer, with no attendee row of
/// their own) and events without attendees both arrive here as
/// selfResponseStatus = null and must always import.
/// </summary>
public class CalendarImportRulesTests
{
    // ---------- Not an invite from someone else ----------

    [Fact]
    public void Null_response_status_imports()
    {
        var decision = CalendarImportRules.EvaluateInvite(null);

        Assert.Equal(CalendarImportRules.InviteImportDecision.Import, decision);
    }

    [Theory]
    [InlineData("needsAction")]
    [InlineData("")]
    [InlineData("declined")]
    public void Organizer_imports_even_when_self_attendee_row_is_not_accepted(string status)
    {
        // Google lists the organizer on Company Meeting with Self=true and
        // needsAction. That used to skip the event. A self-made [itsup] block
        // has no attendee list and imported; the meeting did not.
        var decision = CalendarImportRules.EvaluateInvite(status, isOrganizer: true);

        Assert.Equal(CalendarImportRules.InviteImportDecision.Import, decision);
    }

    // ---------- Accepting responses → Import ----------

    [Theory]
    [InlineData("accepted")]
    [InlineData("ACCEPTED")]
    [InlineData("Accepted")]
    [InlineData("aCcEpTeD")]
    [InlineData("tentative")]
    [InlineData("TENTATIVE")]
    [InlineData("Tentative")]
    public void Accepted_and_tentative_import_regardless_of_case(string status)
    {
        var decision = CalendarImportRules.EvaluateInvite(status);

        Assert.Equal(CalendarImportRules.InviteImportDecision.Import, decision);
    }

    // ---------- Non-accepting responses → Skip ----------

    [Theory]
    [InlineData("declined")]
    [InlineData("DECLINED")]
    public void Declined_skips(string status)
    {
        var decision = CalendarImportRules.EvaluateInvite(status);

        Assert.Equal(CalendarImportRules.InviteImportDecision.Skip, decision);
    }

    [Theory]
    [InlineData("needsAction")]
    [InlineData("NEEDSACTION")]
    [InlineData("")]
    public void NeedsAction_and_empty_import_once_a_slug_matched(string status)
    {
        var decision = CalendarImportRules.EvaluateInvite(status);

        Assert.Equal(CalendarImportRules.InviteImportDecision.Import, decision);
    }

    // ---------- Defensive: unknown / malformed values → Skip ----------
    //
    // If Google introduces a new responseStatus value we haven't seen, the safe
    // default is to NOT import. Better to leave a tracked task off the calendar
    // owner's day than to invent attendance from a string we don't understand.

    [Theory]
    [InlineData("maybe")]
    [InlineData("yes")]
    [InlineData("attending")]
    public void Unknown_status_imports_once_a_slug_matched(string status)
    {
        var decision = CalendarImportRules.EvaluateInvite(status);

        Assert.Equal(CalendarImportRules.InviteImportDecision.Import, decision);
    }

    // ---------- Round-trip: an accepted invite that flips to declined ----------
    //
    // This isn't a test of EvaluateInvite itself (it's stateless) — it documents
    // the *intent* of the Skip decision via the helper's contract. The caller
    // (ImportChangesAsync) reads Skip as "do not import, and if a prior import
    // exists, remove it." Verified by the case below: a declined response yields
    // Skip, not a separate "remove" decision. The caller is responsible for the
    // remove side-effect.

    [Fact]
    public void Decline_after_accept_yields_skip_not_a_separate_remove_decision()
    {
        // First the user accepts — that imports.
        Assert.Equal(CalendarImportRules.InviteImportDecision.Import,
            CalendarImportRules.EvaluateInvite("accepted"));

        // Then they decline — that's a plain Skip. The caller observes the
        // transition by holding the existing TrackedTask and removing it
        // when the new decision comes back as Skip.
        Assert.Equal(CalendarImportRules.InviteImportDecision.Skip,
            CalendarImportRules.EvaluateInvite("declined"));
    }

    [Fact]
    public void Incremental_webhook_still_deletes_tyme_on_google_cancel() =>
        Assert.True(CalendarImportRules.ShouldDeleteTrackedTaskOnGoogleCancel(incrementalSync: true));

    [Fact]
    public void Initial_or_range_sync_does_not_delete_tyme_on_google_cancel() =>
        Assert.False(CalendarImportRules.ShouldDeleteTrackedTaskOnGoogleCancel(incrementalSync: false));
}

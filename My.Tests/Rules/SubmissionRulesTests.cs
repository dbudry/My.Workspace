using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Exercises <see cref="SubmissionRules.Evaluate"/>. This is the rule layer
/// that decides whether an operation on a tracked task is permitted given
/// the submission state of its month. Every CRUD endpoint on TrackedTask
/// and the alias endpoint funnel through here, so getting these decisions
/// wrong is the difference between billable data staying immutable and
/// silently drifting under managers' feet.
///
/// Invariants under test:
///   1. Alias has *inverted* semantics — it REQUIRES submission, the other
///      operations REQUIRE the month to NOT be submitted.
///   2. Each blocked decision returns the correct user-facing reason
///      string (these strings show up in toasts and API error bodies).
///   3. Allowed decisions return Reason = null.
/// </summary>
public class SubmissionRulesTests
{
    // ---------- Alias: requires the month to be submitted ----------

    [Fact]
    public void Alias_is_allowed_when_month_is_submitted()
    {
        var decision = SubmissionRules.Evaluate(SubmissionRules.Operation.Alias, isMonthSubmitted: true);

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void Manager_direct_edit_requires_submitted_month()
    {
        var allowed = SubmissionRules.Evaluate(SubmissionRules.Operation.ManagerDirectEdit, isMonthSubmitted: true);
        var blocked = SubmissionRules.Evaluate(SubmissionRules.Operation.ManagerDirectEdit, isMonthSubmitted: false);

        Assert.True(allowed.IsAllowed);
        Assert.False(blocked.IsAllowed);
    }

    [Fact]
    public void Alias_is_blocked_when_month_is_not_submitted()
    {
        var decision = SubmissionRules.Evaluate(SubmissionRules.Operation.Alias, isMonthSubmitted: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal("This task's month must be submitted before a manager can alter it.", decision.Reason);
    }

    // ---------- Create / Edit / Move / Delete / Duplicate: blocked when submitted ----------

    [Theory]
    [InlineData(SubmissionRules.Operation.Create)]
    [InlineData(SubmissionRules.Operation.Edit)]
    [InlineData(SubmissionRules.Operation.Move)]
    [InlineData(SubmissionRules.Operation.Delete)]
    [InlineData(SubmissionRules.Operation.Duplicate)]
    public void Mutating_operations_are_allowed_when_month_is_not_submitted(SubmissionRules.Operation op)
    {
        var decision = SubmissionRules.Evaluate(op, isMonthSubmitted: false);

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.Reason);
    }

    [Theory]
    [InlineData(SubmissionRules.Operation.Create)]
    [InlineData(SubmissionRules.Operation.Edit)]
    [InlineData(SubmissionRules.Operation.Move)]
    [InlineData(SubmissionRules.Operation.Delete)]
    [InlineData(SubmissionRules.Operation.Duplicate)]
    public void Mutating_operations_are_blocked_when_month_is_submitted(SubmissionRules.Operation op)
    {
        var decision = SubmissionRules.Evaluate(op, isMonthSubmitted: true);

        Assert.False(decision.IsAllowed);
        Assert.False(string.IsNullOrEmpty(decision.Reason));
    }

    // ---------- Reason strings (each operation has its own message) ----------

    [Fact]
    public void Create_block_reason_mentions_submitted_and_unsubmit()
    {
        var reason = SubmissionRules.Evaluate(SubmissionRules.Operation.Create, true).Reason;

        Assert.Equal("This month has already been submitted — unsubmit it first to add new time entries.", reason);
    }

    [Fact]
    public void Edit_block_reason_mentions_submitted_month_and_edit()
    {
        var reason = SubmissionRules.Evaluate(SubmissionRules.Operation.Edit, true).Reason;

        Assert.Equal("This task is in a submitted month and cannot be edited.", reason);
    }

    [Fact]
    public void Move_block_reason_mentions_moving_into_submitted()
    {
        var reason = SubmissionRules.Evaluate(SubmissionRules.Operation.Move, true).Reason;

        Assert.Equal("Cannot move this task into a submitted month.", reason);
    }

    [Fact]
    public void Delete_block_reason_mentions_submitted_and_delete()
    {
        var reason = SubmissionRules.Evaluate(SubmissionRules.Operation.Delete, true).Reason;

        Assert.Equal("This task is in a submitted month and cannot be deleted.", reason);
    }

    [Fact]
    public void Duplicate_block_reason_mentions_duplicate_and_submitted()
    {
        var reason = SubmissionRules.Evaluate(SubmissionRules.Operation.Duplicate, true).Reason;

        Assert.Equal("Cannot duplicate into a submitted month.", reason);
    }

    // ---------- Decision record factory methods ----------

    [Fact]
    public void Decision_Allowed_factory_returns_allowed_with_null_reason()
    {
        var d = SubmissionRules.Decision.Allowed();

        Assert.True(d.IsAllowed);
        Assert.Null(d.Reason);
    }

    [Fact]
    public void Decision_Blocked_factory_carries_the_reason()
    {
        var d = SubmissionRules.Decision.Blocked("nope");

        Assert.False(d.IsAllowed);
        Assert.Equal("nope", d.Reason);
    }
}

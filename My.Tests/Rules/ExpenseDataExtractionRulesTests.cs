using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseDataExtractionRulesTests
{
    [Fact]
    public void TryParseEntities_rejects_empty()
    {
        var ok = ExpenseDataExtractionRules.TryParseEntities(null, out var entities, out var error);

        Assert.False(ok);
        Assert.Empty(entities);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParseEntities_parses_valid_list()
    {
        var ok = ExpenseDataExtractionRules.TryParseEntities(
            "Reports,Lines",
            out var entities,
            out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(2, entities.Count);
        Assert.Contains(ExpenseDataExtractionRules.Reports, entities);
        Assert.Contains(ExpenseDataExtractionRules.Lines, entities);
    }

    [Fact]
    public void TryParseEntities_rejects_unknown()
    {
        var ok = ExpenseDataExtractionRules.TryParseEntities("Reports,Bogus", out _, out var error);

        Assert.False(ok);
        Assert.Contains("Bogus", error);
    }

    [Fact]
    public void TryParseStatus_defaults_to_submitted()
    {
        var ok = ExpenseDataExtractionRules.TryParseStatus(null, out var status, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(ExpenseDataExtractionRules.StatusSubmitted, status);
    }

    [Fact]
    public void TryParseStatus_rejects_unknown()
    {
        var ok = ExpenseDataExtractionRules.TryParseStatus("paid", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateRequest_month_requires_year()
    {
        var error = ExpenseDataExtractionRules.ValidateRequest(
            [ExpenseDataExtractionRules.Lines],
            year: null,
            month: 8);

        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateRequest_accepts_year_and_lines()
    {
        var error = ExpenseDataExtractionRules.ValidateRequest(
            [ExpenseDataExtractionRules.Lines],
            year: 2026,
            month: 8);

        Assert.Null(error);
    }

    [Fact]
    public void MatchesStatus_filters_draft_submitted_and_reimbursed()
    {
        Assert.True(ExpenseDataExtractionRules.MatchesStatus(
            ExpenseStatusRules.Draft, ExpenseDataExtractionRules.StatusAll));
        Assert.True(ExpenseDataExtractionRules.MatchesStatus(
            ExpenseStatusRules.Draft, ExpenseDataExtractionRules.StatusDraft));
        Assert.False(ExpenseDataExtractionRules.MatchesStatus(
            ExpenseStatusRules.Draft, ExpenseDataExtractionRules.StatusSubmitted));
        Assert.True(ExpenseDataExtractionRules.MatchesStatus(
            ExpenseStatusRules.Submitted, ExpenseDataExtractionRules.StatusSubmitted));
        Assert.False(ExpenseDataExtractionRules.MatchesStatus(
            ExpenseStatusRules.Reimbursed, ExpenseDataExtractionRules.StatusSubmitted));
        Assert.True(ExpenseDataExtractionRules.MatchesStatus(
            ExpenseStatusRules.Reimbursed, ExpenseDataExtractionRules.StatusReimbursed));
        Assert.True(ExpenseDataExtractionRules.MatchesStatus(
            ExpenseStatusRules.Reimbursed, ExpenseDataExtractionRules.StatusAll));
    }

    [Fact]
    public void TryParseStatus_accepts_reimbursed()
    {
        var ok = ExpenseDataExtractionRules.TryParseStatus(
            "reimbursed", out var status, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(ExpenseDataExtractionRules.StatusReimbursed, status);
    }

    [Fact]
    public void StoredStatusForFilter_maps_query_to_stored_status()
    {
        Assert.Null(ExpenseDataExtractionRules.StoredStatusForFilter(ExpenseDataExtractionRules.StatusAll));
        Assert.Equal(ExpenseStatusRules.Draft,
            ExpenseDataExtractionRules.StoredStatusForFilter(ExpenseDataExtractionRules.StatusDraft));
        Assert.Equal(ExpenseStatusRules.Submitted,
            ExpenseDataExtractionRules.StoredStatusForFilter(ExpenseDataExtractionRules.StatusSubmitted));
        Assert.Equal(ExpenseStatusRules.Reimbursed,
            ExpenseDataExtractionRules.StoredStatusForFilter(ExpenseDataExtractionRules.StatusReimbursed));
    }
}

using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class TymeDataExtractionRulesTests
{
    [Fact]
    public void TryParseEntities_rejects_empty()
    {
        var ok = TymeDataExtractionRules.TryParseEntities(null, out var entities, out var error);

        Assert.False(ok);
        Assert.Empty(entities);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParseEntities_parses_valid_list()
    {
        var ok = TymeDataExtractionRules.TryParseEntities(
            "Organizations,Projects",
            out var entities,
            out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(2, entities.Count);
        Assert.Contains(TymeDataExtractionRules.Organizations, entities);
        Assert.Contains(TymeDataExtractionRules.Projects, entities);
    }

    [Fact]
    public void TryParseEntities_rejects_unknown_entity()
    {
        var ok = TymeDataExtractionRules.TryParseEntities("Organizations,Bogus", out _, out var error);

        Assert.False(ok);
        Assert.Contains("Bogus", error);
    }

    [Fact]
    public void ValidateRequest_reference_only_does_not_require_dates()
    {
        var error = TymeDataExtractionRules.ValidateRequest(
            [TymeDataExtractionRules.Organizations],
            null,
            null);

        Assert.Null(error);
    }

    [Fact]
    public void ValidateRequest_transactional_requires_dates()
    {
        var error = TymeDataExtractionRules.ValidateRequest(
            [TymeDataExtractionRules.TrackedTasks],
            null,
            null);

        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateDateRange_rejects_range_over_max_days()
    {
        var from = new DateTime(2024, 1, 1);
        var to = from.AddDays(TymeDataExtractionRules.MaxDateRangeDays + 1);

        var error = TymeDataExtractionRules.ValidateDateRange(from, to);

        Assert.NotNull(error);
        Assert.Contains("731", error);
    }

    [Fact]
    public void SubmissionMonthOverlapsRange_matches_month_inside_range()
    {
        var from = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 3, 20, 23, 59, 59, DateTimeKind.Utc);

        Assert.True(TymeDataExtractionRules.SubmissionMonthOverlapsRange(2026, 3, from, to));
        Assert.False(TymeDataExtractionRules.SubmissionMonthOverlapsRange(2026, 2, from, to));
    }

    [Theory]
    [InlineData("Organizations", false, false, true, true, false, false)]
    [InlineData("Projects", false, false, true, true, true, true)]
    [InlineData("ProjectGroups", false, false, false, false, false, true)]
    [InlineData("ApplicationUsers", false, true, true, false, false, false)]
    [InlineData("TrackedTasks", true, true, false, true, true, true)]
    [InlineData("TimeSubmissions", true, true, false, false, false, false)]
    [InlineData("TrackedTaskCorrectionAudits", true, true, false, false, false, false)]
    [InlineData("TrackedTasks,TrackedTaskCorrectionAudits", true, true, false, true, true, true)]
    public void Filter_support_matches_selected_dataset(
        string entitiesCsv,
        bool dateRange,
        bool employees,
        bool includeArchived,
        bool organizations,
        bool project,
        bool projectGroup)
    {
        var entities = entitiesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Equal(dateRange, TymeDataExtractionRules.RequiresDateRange(entities));
        Assert.Equal(employees, TymeDataExtractionRules.SupportsEmployeesFilter(entities));
        Assert.Equal(includeArchived, TymeDataExtractionRules.SupportsIncludeArchivedFilter(entities));
        Assert.Equal(organizations, TymeDataExtractionRules.SupportsOrganizationFilter(entities));
        Assert.Equal(project, TymeDataExtractionRules.SupportsProjectFilter(entities));
        Assert.Equal(projectGroup, TymeDataExtractionRules.SupportsProjectGroupFilter(entities));
    }
}
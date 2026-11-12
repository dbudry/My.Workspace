using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class CrmRulesTests
{
    [Theory]
    [InlineData("open", CrmStageRules.Open)]
    [InlineData("CLOSED WON", CrmStageRules.ClosedWon)]
    [InlineData("closed lost", CrmStageRules.ClosedLost)]
    public void Outcome_accepts_known_values_ignoring_case(string input, string expected)
    {
        Assert.True(CrmStageRules.TryCanonicalOutcome(input, out var canonical));
        Assert.Equal(expected, canonical);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Won")]
    public void Outcome_rejects_unknown_values(string? input)
    {
        Assert.False(CrmStageRules.TryCanonicalOutcome(input, out _));
    }

    [Fact]
    public void Expected_revenue_rounds_half_away_from_zero()
    {
        Assert.Equal(5.01m, CrmStageRules.ExpectedRevenue(10.01m, 50));
        Assert.Equal(0.01m, CrmStageRules.ExpectedRevenue(0.01m, 50));
        Assert.Equal(0m, CrmStageRules.ExpectedRevenue(null, 50));
    }

    [Fact]
    public void Closing_date_prefers_actual()
    {
        var actual = new DateTime(2026, 3, 1);
        var forecast = new DateTime(2026, 4, 1);
        Assert.Equal(actual, CrmStageRules.ClosingDate(actual, forecast));
        Assert.Equal(forecast, CrmStageRules.ClosingDate(null, forecast));
    }

    [Fact]
    public void Actual_close_fills_today_only_when_the_stage_becomes_closed()
    {
        var today = new DateTime(2026, 5, 2);
        Assert.Equal(today, CrmStageRules.ResolveActualClose(true, CrmStageRules.ClosedWon, null, today));
        Assert.Equal(today, CrmStageRules.ResolveActualClose(true, CrmStageRules.ClosedLost, null, today));
        Assert.Null(CrmStageRules.ResolveActualClose(true, CrmStageRules.Open, null, today));
        Assert.Null(CrmStageRules.ResolveActualClose(false, CrmStageRules.ClosedWon, null, today));
        var kept = new DateTime(2026, 1, 9);
        Assert.Equal(kept, CrmStageRules.ResolveActualClose(true, CrmStageRules.ClosedWon, kept, today));
        Assert.Null(CrmStageRules.ResolveActualClose(true, CrmStageRules.Open, null, today));
    }

    [Fact]
    public void Seed_stages_keep_lead_and_qualified_and_have_no_second_qualification()
    {
        Assert.Equal(7, CrmStageRules.Seeds.Length);
        Assert.Contains(CrmStageRules.Seeds, stage => stage.Name == "Lead" && stage.DefaultProbability == 5);
        Assert.Contains(CrmStageRules.Seeds, stage => stage.Name == "Qualified" && stage.DefaultProbability == 10);
        Assert.DoesNotContain(CrmStageRules.Seeds, stage => stage.Name == "Qualification");
        Assert.Contains(CrmStageRules.Seeds, stage => stage.Name == "Closed Won" && stage.Outcome == CrmStageRules.ClosedWon);
        Assert.Contains(CrmStageRules.Seeds, stage => stage.Name == "Closed Lost" && stage.Outcome == CrmStageRules.ClosedLost);
    }

    [Fact]
    public void Relationship_types_parse_the_seed_and_keep_decision_maker()
    {
        var types = CrmRelationshipTypeRules.Parse(null);
        Assert.Contains(types, type => type.Name == CrmRelationshipTypeRules.DecisionMaker);
        Assert.Equal(3, types.Count);

        var error = CrmRelationshipTypeRules.ValidateUpdate(
            types.Where(type => type.Name != CrmRelationshipTypeRules.DecisionMaker).ToList(),
            new Dictionary<string, int>());
        Assert.Contains(CrmRelationshipTypeRules.DecisionMaker, error);
    }

    [Fact]
    public void Relationship_type_in_use_cannot_be_removed()
    {
        var types = CrmRelationshipTypeRules.Parse(CrmRelationshipTypeRules.DefaultJson)
            .Where(type => type.Name != "Assessor")
            .ToList();
        var error = CrmRelationshipTypeRules.ValidateUpdate(
            types,
            new Dictionary<string, int> { ["Assessor"] = 2 });
        Assert.Contains("Assessor", error);
        Assert.Contains("2", error);
    }

    [Fact]
    public void Blank_relationship_type_canonical_is_null()
    {
        var types = CrmRelationshipTypeRules.Defaults;
        Assert.Null(CrmRelationshipTypeRules.Canonical("  ", types));
        Assert.Equal(CrmRelationshipTypeRules.DecisionMaker, CrmRelationshipTypeRules.Canonical("decision maker", types));
    }
}

public class CrmNotifyRecipientRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void Parse_returns_nobody_when_unset_or_bad(string? json)
    {
        Assert.Empty(CrmNotifyRecipientRules.Parse(json));
    }

    [Fact]
    public void Serialize_trims_dedupes_and_sorts()
    {
        var json = CrmNotifyRecipientRules.Serialize(["b", " a ", "b", "", null]);
        Assert.Equal("[\"a\",\"b\"]", json);
        Assert.Equal(["a", "b"], CrmNotifyRecipientRules.Parse(json));
    }
}

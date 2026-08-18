using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Pins the KQL the Logs admin page generates. The bug class to guard against:
/// the query gets sent to App Insights, but the Workspace API rejects unbounded
/// time ranges + Verbose levels with a 400. Clamping happens here so the API
/// endpoint never has to.
/// </summary>
public class LogsQueryRulesTests
{
    // ---------- ParseLevel ----------

    [Theory]
    [InlineData("Verbose", LogsQueryRules.SeverityLevel.Verbose)]
    [InlineData("verbose", LogsQueryRules.SeverityLevel.Verbose)]
    [InlineData("Trace", LogsQueryRules.SeverityLevel.Verbose)]
    [InlineData("0", LogsQueryRules.SeverityLevel.Verbose)]
    [InlineData("Information", LogsQueryRules.SeverityLevel.Information)]
    [InlineData("info", LogsQueryRules.SeverityLevel.Information)]
    [InlineData("1", LogsQueryRules.SeverityLevel.Information)]
    [InlineData("Warning", LogsQueryRules.SeverityLevel.Warning)]
    [InlineData("warn", LogsQueryRules.SeverityLevel.Warning)]
    [InlineData("Error", LogsQueryRules.SeverityLevel.Error)]
    [InlineData("Critical", LogsQueryRules.SeverityLevel.Critical)]
    [InlineData("Fatal", LogsQueryRules.SeverityLevel.Critical)]
    public void Known_level_names_parse_to_expected_enum(string raw, LogsQueryRules.SeverityLevel expected)
    {
        Assert.Equal(expected, LogsQueryRules.ParseLevel(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-level")]
    public void Missing_or_unknown_level_defaults_to_information(string? raw)
    {
        Assert.Equal(LogsQueryRules.SeverityLevel.Information, LogsQueryRules.ParseLevel(raw));
    }

    // ---------- Clamping ----------

    [Theory]
    [InlineData(0, LogsQueryRules.MinHours)]
    [InlineData(-5, LogsQueryRules.MinHours)]
    [InlineData(24, 24)]
    [InlineData(168, 168)]
    [InlineData(999, LogsQueryRules.MaxHours)]
    public void Hours_are_clamped_to_bounds(int input, int expected)
    {
        Assert.Equal(expected, LogsQueryRules.ClampHours(input));
    }

    [Theory]
    [InlineData(0, LogsQueryRules.MinTop)]
    [InlineData(-1, LogsQueryRules.MinTop)]
    [InlineData(200, 200)]
    [InlineData(1000, 1000)]
    [InlineData(50000, LogsQueryRules.MaxTop)]
    public void Top_is_clamped_to_bounds(int input, int expected)
    {
        Assert.Equal(expected, LogsQueryRules.ClampTop(input));
    }

    // ---------- Build ----------

    [Fact]
    public void Build_includes_hours_and_min_severity_filter()
    {
        var kql = LogsQueryRules.Build(24, LogsQueryRules.SeverityLevel.Warning, 200);

        Assert.Contains("ago(24h)", kql);
        Assert.Contains("severityLevel >= 2", kql);
        Assert.Contains("top 200", kql);
    }

    [Fact]
    public void Build_unions_traces_and_exceptions()
    {
        var kql = LogsQueryRules.Build(1, LogsQueryRules.SeverityLevel.Verbose, 100);

        Assert.Contains("traces", kql);
        Assert.Contains("exceptions", kql);
        Assert.Contains("union", kql);
    }

    [Fact]
    public void Build_clamps_oversized_inputs()
    {
        var kql = LogsQueryRules.Build(99999, LogsQueryRules.SeverityLevel.Information, 99999);

        Assert.Contains($"ago({LogsQueryRules.MaxHours}h)", kql);
        Assert.Contains($"top {LogsQueryRules.MaxTop}", kql);
    }

    [Fact]
    public void Build_clamps_undersized_inputs()
    {
        var kql = LogsQueryRules.Build(0, LogsQueryRules.SeverityLevel.Information, 0);

        Assert.Contains($"ago({LogsQueryRules.MinHours}h)", kql);
        Assert.Contains($"top {LogsQueryRules.MinTop}", kql);
    }

    [Fact]
    public void Build_orders_results_newest_first()
    {
        var kql = LogsQueryRules.Build(24, LogsQueryRules.SeverityLevel.Information, 200);
        Assert.Contains("by timestamp desc", kql);
    }

    [Fact]
    public void Build_does_not_use_reserved_kind_as_column_alias()
    {
        // KQL treats `kind` as a reserved query modifier (`union kind=outer`,
        // `find kind=...`). Using it as an unqualified column alias makes the
        // Query API 400 with SYN0002 at the `kind` token. Guard the column
        // name so a future "cleanup" rename can't reintroduce the bug.
        var kql = LogsQueryRules.Build(24, LogsQueryRules.SeverityLevel.Information, 200);

        Assert.DoesNotContain("extend kind =", kql);
        Assert.Contains("extend logKind =", kql);
    }
}

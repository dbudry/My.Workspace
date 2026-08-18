using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// The Logs admin page depends on parsing the ApplicationId out of the App
/// Insights connection string Azure auto-sets. If parsing breaks the page goes
/// dark, so pin the contract.
/// </summary>
public class AppInsightsConnectionStringTests
{
    [Fact]
    public void Extracts_ApplicationId_from_standard_connection_string()
    {
        // Real shape Azure emits when AI is wired to a Function App.
        const string cs = "InstrumentationKey=5402dd70-edcd-45f2-a925-6dd164a3e50f;" +
                          "IngestionEndpoint=https://eastus2-3.in.applicationinsights.azure.com/;" +
                          "LiveEndpoint=https://eastus2.livediagnostics.monitor.azure.com/;" +
                          "ApplicationId=8d739e8e-a9d6-4432-b0c8-340b758d92e8";

        Assert.Equal("8d739e8e-a9d6-4432-b0c8-340b758d92e8",
            AppInsightsConnectionString.GetApplicationId(cs));
    }

    [Fact]
    public void Returns_null_when_ApplicationId_missing()
    {
        // Older AI resources don't include ApplicationId in the connection
        // string — we surface a clear error rather than silently 500ing.
        const string cs = "InstrumentationKey=abc;IngestionEndpoint=https://example.com/";
        Assert.Null(AppInsightsConnectionString.GetApplicationId(cs));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Returns_null_for_blank_input(string? input)
    {
        Assert.Null(AppInsightsConnectionString.GetApplicationId(input));
    }

    [Fact]
    public void Field_key_match_is_case_insensitive()
    {
        // Defensive — the docs say `ApplicationId` but Azure occasionally
        // varies casing across env-var sources.
        const string cs = "applicationid=guid-here";
        Assert.Equal("guid-here", AppInsightsConnectionString.GetApplicationId(cs));
    }

    [Fact]
    public void Trims_whitespace_around_key_and_value()
    {
        const string cs = "  ApplicationId  =   guid-trimmed   ;Other=x";
        Assert.Equal("guid-trimmed", AppInsightsConnectionString.GetApplicationId(cs));
    }

    [Fact]
    public void Ignores_malformed_pairs_without_value()
    {
        // `ApplicationId=` with no value should not be returned as an empty string —
        // an empty App ID would form a broken URL.
        const string cs = "InstrumentationKey=k;ApplicationId=";
        Assert.Null(AppInsightsConnectionString.GetApplicationId(cs));
    }
}

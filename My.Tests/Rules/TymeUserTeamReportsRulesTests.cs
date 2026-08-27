using My.Shared.Constants;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class TymeUserTeamReportsRulesTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", false)]
    public void IsEnabled_parses_workspace_flag(string? value, bool expected)
    {
        Assert.Equal(expected, TymeUserTeamReportsRules.IsEnabled(value));
    }

    [Fact]
    public void IsEnabled_dictionary_reads_setting_key()
    {
        var on = new Dictionary<string, string>
        {
            [Constants.SettingKeys.TymeAllowUserTeamReports] = "true"
        };
        var off = new Dictionary<string, string>
        {
            [Constants.SettingKeys.TymeAllowUserTeamReports] = "false"
        };
        var missing = new Dictionary<string, string>();

        Assert.True(TymeUserTeamReportsRules.IsEnabled(on));
        Assert.False(TymeUserTeamReportsRules.IsEnabled(off));
        Assert.False(TymeUserTeamReportsRules.IsEnabled(missing));
    }

    [Fact]
    public void DefaultEnabled_is_off()
    {
        Assert.False(TymeUserTeamReportsRules.DefaultEnabled);
    }
}

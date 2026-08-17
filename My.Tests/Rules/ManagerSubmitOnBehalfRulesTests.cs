using My.Shared.Constants;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ManagerSubmitOnBehalfRulesTests
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
        Assert.Equal(expected, ManagerSubmitOnBehalfRules.IsEnabled(value));
    }

    [Fact]
    public void IsEnabled_dictionary_reads_setting_key()
    {
        var on = new Dictionary<string, string>
        {
            [Constants.SettingKeys.TymeAllowManagerSubmitOnBehalf] = "true"
        };
        var off = new Dictionary<string, string>
        {
            [Constants.SettingKeys.TymeAllowManagerSubmitOnBehalf] = "false"
        };
        var missing = new Dictionary<string, string>();

        Assert.True(ManagerSubmitOnBehalfRules.IsEnabled(on));
        Assert.False(ManagerSubmitOnBehalfRules.IsEnabled(off));
        Assert.False(ManagerSubmitOnBehalfRules.IsEnabled(missing));
    }

    [Fact]
    public void DefaultEnabled_is_off()
    {
        Assert.False(ManagerSubmitOnBehalfRules.DefaultEnabled);
    }
}

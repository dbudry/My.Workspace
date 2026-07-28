using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class EmployeeTimeDisplayModeRulesTests
{
    [Theory]
    [InlineData(null, EmployeeTimeDisplayMode.Both)]
    [InlineData("", EmployeeTimeDisplayMode.Both)]
    [InlineData("both", EmployeeTimeDisplayMode.Both)]
    [InlineData("their", EmployeeTimeDisplayMode.TheirTime)]
    [InlineData("TheirTime", EmployeeTimeDisplayMode.TheirTime)]
    [InlineData("original", EmployeeTimeDisplayMode.TheirTime)]
    [InlineData("adjusted", EmployeeTimeDisplayMode.Adjusted)]
    [InlineData("manager", EmployeeTimeDisplayMode.Adjusted)]
    public void Parse_maps_known_keys(string? raw, EmployeeTimeDisplayMode expected)
    {
        Assert.Equal(expected, EmployeeTimeDisplayModeRules.Parse(raw));
    }

    [Theory]
    [InlineData(EmployeeTimeDisplayMode.TheirTime, "their")]
    [InlineData(EmployeeTimeDisplayMode.Adjusted, "adjusted")]
    [InlineData(EmployeeTimeDisplayMode.Both, "both")]
    public void ToStorageKey_round_trips(EmployeeTimeDisplayMode mode, string key)
    {
        Assert.Equal(key, EmployeeTimeDisplayModeRules.ToStorageKey(mode));
        Assert.Equal(mode, EmployeeTimeDisplayModeRules.Parse(key));
    }

    [Fact]
    public void IncludeOriginal_TheirTime_always()
    {
        Assert.True(EmployeeTimeDisplayModeRules.IncludeOriginal(EmployeeTimeDisplayMode.TheirTime, hasAdjustment: true));
        Assert.True(EmployeeTimeDisplayModeRules.IncludeOriginal(EmployeeTimeDisplayMode.TheirTime, hasAdjustment: false));
    }

    [Fact]
    public void IncludeOriginal_Adjusted_only_when_no_adjustment()
    {
        Assert.False(EmployeeTimeDisplayModeRules.IncludeOriginal(EmployeeTimeDisplayMode.Adjusted, hasAdjustment: true));
        Assert.True(EmployeeTimeDisplayModeRules.IncludeOriginal(EmployeeTimeDisplayMode.Adjusted, hasAdjustment: false));
    }

    [Fact]
    public void IncludeOriginal_Both_always()
    {
        Assert.True(EmployeeTimeDisplayModeRules.IncludeOriginal(EmployeeTimeDisplayMode.Both, hasAdjustment: true));
        Assert.True(EmployeeTimeDisplayModeRules.IncludeOriginal(EmployeeTimeDisplayMode.Both, hasAdjustment: false));
    }

    [Fact]
    public void IncludeAdjustmentOverlay_only_when_adjusted_or_both_and_has_adjustment()
    {
        Assert.False(EmployeeTimeDisplayModeRules.IncludeAdjustmentOverlay(EmployeeTimeDisplayMode.TheirTime, true));
        Assert.True(EmployeeTimeDisplayModeRules.IncludeAdjustmentOverlay(EmployeeTimeDisplayMode.Adjusted, true));
        Assert.True(EmployeeTimeDisplayModeRules.IncludeAdjustmentOverlay(EmployeeTimeDisplayMode.Both, true));
        Assert.False(EmployeeTimeDisplayModeRules.IncludeAdjustmentOverlay(EmployeeTimeDisplayMode.Both, false));
    }

    [Fact]
    public void FromAppSettings_reads_workspace_default()
    {
        var settings = new Dictionary<string, string>
        {
            [My.Shared.Constants.Constants.SettingKeys.TymeEmployeeTimeDisplayMode] = "their"
        };
        Assert.Equal(EmployeeTimeDisplayMode.TheirTime, EmployeeTimeDisplayModeRules.FromAppSettings(settings));
        Assert.Equal(EmployeeTimeDisplayMode.Both, EmployeeTimeDisplayModeRules.FromAppSettings(
            new Dictionary<string, string>()));
    }
}

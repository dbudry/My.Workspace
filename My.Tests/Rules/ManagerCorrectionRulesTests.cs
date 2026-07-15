using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ManagerCorrectionRulesTests
{
    [Fact]
    public void Parse_defaults_when_keys_missing()
    {
        var settings = ManagerCorrectionRules.Parse(new Dictionary<string, string>());

        Assert.True(settings.AllowManagerCorrection);
        Assert.Equal(ManagerCorrectionRules.CorrectionMode.Alias, settings.Mode);
        Assert.True(settings.CanCreateOrUpdateAlias);
        Assert.False(settings.CanCreateOrUpdateDirect);
    }

    [Fact]
    public void ParseMode_direct_when_specified()
    {
        Assert.Equal(ManagerCorrectionRules.CorrectionMode.Direct,
            ManagerCorrectionRules.ParseMode("Direct"));
    }

    [Fact]
    public void ValidateSettings_allows_master_disabled()
    {
        var decision = ManagerCorrectionRules.ValidateSettings(false, ManagerCorrectionRules.CorrectionMode.Alias);
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Evaluate_allows_alias_only_when_mode_is_alias()
    {
        var settings = new ManagerCorrectionSettings(true, ManagerCorrectionRules.CorrectionMode.Alias);

        Assert.True(ManagerCorrectionRules.Evaluate(
            ManagerCorrectionRules.CorrectionMode.Alias,
            ManagerCorrectionRules.CorrectionAction.Create,
            settings).IsAllowed);

        Assert.False(ManagerCorrectionRules.Evaluate(
            ManagerCorrectionRules.CorrectionMode.Direct,
            ManagerCorrectionRules.CorrectionAction.Create,
            settings).IsAllowed);
    }

    [Fact]
    public void Evaluate_allows_direct_only_when_mode_is_direct()
    {
        var settings = new ManagerCorrectionSettings(true, ManagerCorrectionRules.CorrectionMode.Direct);

        Assert.True(ManagerCorrectionRules.Evaluate(
            ManagerCorrectionRules.CorrectionMode.Direct,
            ManagerCorrectionRules.CorrectionAction.Create,
            settings).IsAllowed);

        Assert.False(ManagerCorrectionRules.Evaluate(
            ManagerCorrectionRules.CorrectionMode.Alias,
            ManagerCorrectionRules.CorrectionAction.Create,
            settings).IsAllowed);
    }

    [Fact]
    public void Evaluate_always_allows_delete()
    {
        var settings = new ManagerCorrectionSettings(false, ManagerCorrectionRules.CorrectionMode.Alias);
        var decision = ManagerCorrectionRules.Evaluate(
            ManagerCorrectionRules.CorrectionMode.Alias,
            ManagerCorrectionRules.CorrectionAction.Delete,
            settings);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Evaluate_blocks_all_creates_when_master_disabled()
    {
        var settings = new ManagerCorrectionSettings(false, ManagerCorrectionRules.CorrectionMode.Alias);
        var decision = ManagerCorrectionRules.Evaluate(
            ManagerCorrectionRules.CorrectionMode.Alias,
            ManagerCorrectionRules.CorrectionAction.Create,
            settings);

        Assert.False(decision.IsAllowed);
    }
}
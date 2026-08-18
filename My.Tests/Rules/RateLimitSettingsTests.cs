using My.Shared.Constants;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class RateLimitSettingsTests
{
    [Theory]
    [InlineData("true", null, true)]
    [InlineData("false", "true", false)]
    [InlineData(null, "true", true)]
    [InlineData(null, null, false)]
    [InlineData("  true  ", "false", true)]
    public void Resolve_prefers_database_over_configuration(
        string? dbRaw,
        string? configRaw,
        bool expected) =>
        Assert.Equal(expected, RateLimitSettings.Resolve(dbRaw, configRaw).Enabled);

    [Fact]
    public void ParseFromAppSettingsRows_reads_rate_limit_key()
    {
        IEnumerable<(string Key, string? Value)> rows = new[]
        {
            (Key: Constants.SettingKeys.AllowUserDelete, Value: (string?)"true"),
            (Key: Constants.SettingKeys.RateLimitEnabled, Value: (string?)"true"),
        };

        Assert.True(RateLimitSettings.ParseFromAppSettingsRows(rows).Enabled);
    }

    [Fact]
    public void ParseFromAppSettingsRows_reads_custom_limits()
    {
        IEnumerable<(string Key, string? Value)> rows = new[]
        {
            (Key: Constants.SettingKeys.RateLimitEnabled, Value: (string?)"true"),
            (Key: Constants.SettingKeys.RateLimitAuthenticatedPerMinute, Value: (string?)"150"),
            (Key: Constants.SettingKeys.RateLimitUploadPerMinute, Value: (string?)"7"),
            (Key: Constants.SettingKeys.RateLimitProvisionPerMinute, Value: (string?)"0"), // clamp to min
        };

        var settings = RateLimitSettings.ParseFromAppSettingsRows(rows);

        Assert.True(settings.Enabled);
        Assert.Equal(150, settings.AuthenticatedPerMinute);
        Assert.Equal(7, settings.UploadPerMinute);
        Assert.Equal(RateLimitSettings.MinPermitsPerMinute, settings.ProvisionPerMinute);
        Assert.Equal(RateLimitRules.DefaultAnonymousPerMinute, settings.AnonymousPerMinute);

        var options = settings.ToOptions();
        Assert.True(options.Enabled);
        Assert.Equal(150, options.DefaultAuthenticatedPerMinute);
        Assert.Equal(7, options.UploadPerMinute);
    }

    [Fact]
    public void ToOptions_uses_defaults_when_limits_missing()
    {
        var settings = RateLimitSettings.Resolve("true", null);
        var options = settings.ToOptions();

        Assert.Equal(RateLimitRules.DefaultAuthenticatedPerMinute, options.DefaultAuthenticatedPerMinute);
        Assert.Equal(RateLimitRules.UploadPerMinute, options.UploadPerMinute);
    }
}

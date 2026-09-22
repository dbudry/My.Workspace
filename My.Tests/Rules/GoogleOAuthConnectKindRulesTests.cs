using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleOAuthConnectKindRulesTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("calendar", false)]
    [InlineData("drive", true)]
    [InlineData("Drive", true)]
    [InlineData("appdrive", false)]
    public void IsDrive_only_for_drive_kind(string? kind, bool expected) =>
        Assert.Equal(expected, GoogleOAuthConnectKindRules.IsDrive(kind));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("calendar", false)]
    [InlineData("drive", false)]
    [InlineData("appdrive", true)]
    [InlineData("AppDrive", true)]
    public void IsAppDrive_only_for_appdrive_kind(string? kind, bool expected) =>
        Assert.Equal(expected, GoogleOAuthConnectKindRules.IsAppDrive(kind));
}
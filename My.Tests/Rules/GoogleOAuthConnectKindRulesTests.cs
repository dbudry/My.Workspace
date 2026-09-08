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
    public void IsDrive_only_for_drive_kind(string? kind, bool expected) =>
        Assert.Equal(expected, GoogleOAuthConnectKindRules.IsDrive(kind));
}
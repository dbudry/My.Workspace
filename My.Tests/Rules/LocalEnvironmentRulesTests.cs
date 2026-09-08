using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class LocalEnvironmentRulesTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("localhost:7047")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:7074")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    [InlineData("[::1]:7047")]
    public void Loopback_hosts_are_local(string host) =>
        Assert.True(LocalEnvironmentRules.IsLocalHost(host));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("app.example.com")]
    [InlineData("something.azurestaticapps.net")]
    [InlineData("func-example.azurewebsites.net")]
    public void Production_and_empty_are_not_local(string? host) =>
        Assert.False(LocalEnvironmentRules.IsLocalHost(host));

    [Theory]
    [InlineData("localhost", "7047", true)]
    [InlineData("localhost", "5047", true)]
    [InlineData("localhost", "11256", true)]
    [InlineData("127.0.0.1", "7047", true)]
    [InlineData("localhost", "443", false)]
    [InlineData("localhost", "", false)]
    [InlineData("localhost", "80", false)]
    [InlineData("app.example.com", "443", false)]
    [InlineData("app.example.com", "7047", false)]
    [InlineData("something.azurestaticapps.net", "443", false)]
    [InlineData("func-example.azurewebsites.net", "443", false)]
    [InlineData("something.azurestaticapps.net", "7047", false)]
    public void Local_debug_client_requires_loopback_and_dev_port(
        string? host, string port, bool expected) =>
        Assert.Equal(expected, LocalEnvironmentRules.IsLocalDebugClient(host, port));

    [Fact]
    public void Local_debug_uri_matches_dev_start_https() =>
        Assert.True(LocalEnvironmentRules.IsLocalDebugClient(new Uri("https://localhost:7047/")));

    [Fact]
    public void Production_uri_is_never_local_debug() =>
        Assert.False(LocalEnvironmentRules.IsLocalDebugClient(new Uri("https://app.example.com/")));
}

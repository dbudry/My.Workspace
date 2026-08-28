using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleCalendarStorageErrorRulesTests
{
    [Fact]
    public void Format_includes_code_and_status() =>
        Assert.Equal(
            "AuthenticationFailed (403): Server failed to authenticate the request.",
            GoogleCalendarStorageErrorRules.Format(403, "AuthenticationFailed", "Server failed to authenticate the request."));

    [Fact]
    public void Format_status_only() =>
        Assert.Equal("HTTP 503: unavailable", GoogleCalendarStorageErrorRules.Format(503, null, "unavailable"));

    [Fact]
    public void Format_falls_back_when_empty() =>
        Assert.Equal("Storage call failed.", GoogleCalendarStorageErrorRules.Format(null, "  ", "  "));

    [Theory]
    [InlineData(0, true)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    public void Transient_statuses(int status, bool expected) =>
        Assert.Equal(expected, GoogleCalendarStorageErrorRules.IsTransientStatus(status));

    [Theory]
    [InlineData(409, null, true)]
    [InlineData(412, "LeaseIdMissing", true)]
    [InlineData(409, "BlobAlreadyExists", true)]
    [InlineData(403, null, false)]
    public void Lock_blob_already_present(int status, string? code, bool expected) =>
        Assert.Equal(expected, GoogleCalendarStorageErrorRules.IsLockBlobAlreadyPresent(status, code));

    [Theory]
    [InlineData(409, "LeaseAlreadyPresent", true)]
    [InlineData(412, "LeaseIdMissing", true)]
    [InlineData(500, null, false)]
    public void Lease_held(int status, string? code, bool expected) =>
        Assert.Equal(expected, GoogleCalendarStorageErrorRules.IsLeaseHeld(status, code));
}

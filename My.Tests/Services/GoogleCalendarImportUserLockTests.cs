using My.Functions.Services;
using Xunit;

namespace My.Tests.Services;

public class GoogleCalendarImportUserLockTests
{
    [Theory]
    [InlineData("abc-123", "abc-123")]
    [InlineData("user@x.com", "user-x-com")]
    [InlineData("", "_unknown")]
    [InlineData("   ", "_unknown")]
    public void SanitizeBlobName_is_blob_safe(string userId, string expected) =>
        Assert.Equal(expected, GoogleCalendarImportUserLock.SanitizeBlobName(userId));
}

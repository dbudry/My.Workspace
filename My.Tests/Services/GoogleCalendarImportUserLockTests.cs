using My.Functions.Services;
using Xunit;

namespace My.Tests.Services;

public class GoogleCalendarImportUserLockTests
{
    [Fact]
    public async Task TryWaitForLeaseAsync_returns_false_when_lease_stays_held()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var acquired = await GoogleCalendarImportUserLock.TryWaitForLeaseAsync(
            _ => Task.FromResult(false),
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);
        sw.Stop();

        Assert.False(acquired);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TryWaitForLeaseAsync_returns_true_on_first_success()
    {
        var acquired = await GoogleCalendarImportUserLock.TryWaitForLeaseAsync(
            _ => Task.FromResult(true),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.True(acquired);
    }

    [Theory]
    [InlineData("abc-123", "abc-123")]
    [InlineData("user@x.com", "user-x-com")]
    [InlineData("", "_unknown")]
    [InlineData("   ", "_unknown")]
    public void SanitizeBlobName_is_blob_safe(string userId, string expected) =>
        Assert.Equal(expected, GoogleCalendarImportUserLock.SanitizeBlobName(userId));
}

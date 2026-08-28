using My.Functions.Services;
using Xunit;

namespace My.Tests.Services;

public class GoogleCalendarImportQueueTests
{
    [Fact]
    public void DescribeError_uses_exception_message() =>
        Assert.Equal(
            "queue is down",
            GoogleCalendarImportQueue.DescribeError(new InvalidOperationException("queue is down")));

    [Fact]
    public void DescribeError_cancel_is_cold_start_guidance()
    {
        var text = GoogleCalendarImportQueue.DescribeError(new TaskCanceledException("The operation was canceled."));
        Assert.Contains("cold start", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_reads_camelCase_json()
    {
        var msg = GoogleCalendarImportQueue.TryParse(
            """{"channelId":"ch-1","channelToken":"tok","resourceState":"exists"}""");

        Assert.NotNull(msg);
        Assert.Equal("ch-1", msg!.ChannelId);
        Assert.Equal("tok", msg.ChannelToken);
        Assert.Equal("exists", msg.ResourceState);
    }

    [Fact]
    public void TryParse_reads_PascalCase_json()
    {
        var msg = GoogleCalendarImportQueue.TryParse(
            """{"ChannelId":"ch-1","ChannelToken":"tok","ResourceState":"exists"}""");

        Assert.NotNull(msg);
        Assert.Equal("ch-1", msg!.ChannelId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    [InlineData("{}")]
    public void TryParse_returns_null_or_empty_channel_for_junk(string? raw)
    {
        var msg = GoogleCalendarImportQueue.TryParse(raw);
        if (raw == "{}")
        {
            Assert.NotNull(msg);
            Assert.Equal("", msg!.ChannelId);
        }
        else
        {
            Assert.Null(msg);
        }
    }
}

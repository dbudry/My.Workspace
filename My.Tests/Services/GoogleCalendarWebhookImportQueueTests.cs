using My.Functions.Services;
using Xunit;

namespace My.Tests.Services;

public class GoogleCalendarWebhookImportQueueTests
{
    [Fact]
    public void TryEnqueue_rejects_empty_and_dedupes_same_user()
    {
        var queue = new GoogleCalendarWebhookImportQueue();

        Assert.False(queue.TryEnqueue(""));
        Assert.True(queue.TryEnqueue("user-1"));
        Assert.False(queue.TryEnqueue("user-1"));
        Assert.True(queue.TryEnqueue("user-2"));
    }

    [Fact]
    public void MarkComplete_allows_the_user_to_queue_again()
    {
        var queue = new GoogleCalendarWebhookImportQueue();

        Assert.True(queue.TryEnqueue("user-1"));
        queue.MarkComplete("user-1");
        Assert.True(queue.TryEnqueue("user-1"));
    }
}

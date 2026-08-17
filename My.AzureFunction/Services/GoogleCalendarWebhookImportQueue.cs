using System.Collections.Concurrent;
using System.Threading.Channels;

namespace My.Functions.Services;

/// <summary>
/// In-process queue so the Google webhook HTTP trigger can return 200 immediately.
/// A background worker then runs the incremental import. Dedupes by user so a
/// burst of notifications collapses to one import.
/// </summary>
public sealed class GoogleCalendarWebhookImportQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);

    public bool TryEnqueue(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        if (!_pending.TryAdd(userId, 0))
            return false;

        if (_channel.Writer.TryWrite(userId))
            return true;

        _pending.TryRemove(userId, out _);
        return false;
    }

    public ChannelReader<string> Reader => _channel.Reader;

    public void MarkComplete(string userId) => _pending.TryRemove(userId, out _);
}

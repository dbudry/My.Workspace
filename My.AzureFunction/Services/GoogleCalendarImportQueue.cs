using System.Text.Json;
using Azure;
using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;
using My.Shared.Constants;
using My.Shared.Dtos.GoogleCalendar;
using My.Shared.Rules;

namespace My.Functions.Services;

/// <summary>
/// Durable enqueue for Google Calendar webhooks. Uses the Function App
/// <c>AzureWebJobsStorage</c> account. The HTTP
/// trigger must not wait on SQL; this write is Storage-only.
/// </summary>
public sealed class GoogleCalendarImportQueue
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Isolated-worker HTTP <c>FunctionContext.CancellationToken</c> is cancelled
    /// on client abort and during cold start — the same window where the first
    /// queue call is slow. Webhook import must not depend on that token.
    /// </summary>
    private static readonly TimeSpan EnqueueTimeout = TimeSpan.FromSeconds(30);

    private readonly QueueClient _queue;
    private readonly ILogger<GoogleCalendarImportQueue> _logger;

    public GoogleCalendarImportQueue(QueueServiceClient queueService, ILogger<GoogleCalendarImportQueue> logger)
    {
        _queue = queueService.GetQueueClient(Constants.API.GoogleCalendar.ImportQueue);
        _logger = logger;
    }

    public async Task EnqueueAsync(GoogleCalendarImportQueueMessage message, CancellationToken cancellationToken)
    {
        // Do not honor the HTTP request token. See EnqueueTimeout.
        _ = cancellationToken;

        var json = JsonSerializer.Serialize(message, JsonOptions);
        Exception? last = null;
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var cts = new CancellationTokenSource(EnqueueTimeout);
            try
            {
                await SendOrCreateAsync(json, cts.Token);
                _logger.LogInformation(
                    GoogleCalendarLogEvents.Enqueued,
                    "Enqueued Google calendar import for channel {ChannelId}.", message.ChannelId);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsRetryable(ex))
            {
                last = ex;
                _logger.LogWarning(
                    GoogleCalendarLogEvents.EnqueueFailed,
                    "Google calendar import enqueue transient failure (attempt {Attempt}/{Max}): {Error}",
                    attempt, maxAttempts, DescribeError(ex));
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt));
            }
        }

        throw last ?? new InvalidOperationException("Google calendar import enqueue failed.");
    }

    public static string DescribeError(Exception ex)
    {
        if (ex is OperationCanceledException)
            return "Storage timed out or the request was cancelled (often a cold start). Try again.";
        if (ex is RequestFailedException rfe)
            return GoogleCalendarStorageErrorRules.Format(rfe.Status, rfe.ErrorCode, rfe.Message);
        return string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message.Trim();
    }

    private static bool IsRetryable(Exception ex) =>
        ex is OperationCanceledException
        || (ex is RequestFailedException rfe && GoogleCalendarStorageErrorRules.IsTransientStatus(rfe.Status));

    private async Task SendOrCreateAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            await _queue.SendMessageAsync(json, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (
            ex.Status == 404
            || string.Equals(ex.ErrorCode, "QueueNotFound", StringComparison.OrdinalIgnoreCase))
        {
            await _queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            await _queue.SendMessageAsync(json, cancellationToken: cancellationToken);
        }
    }

    public static GoogleCalendarImportQueueMessage? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            return JsonSerializer.Deserialize<GoogleCalendarImportQueueMessage>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

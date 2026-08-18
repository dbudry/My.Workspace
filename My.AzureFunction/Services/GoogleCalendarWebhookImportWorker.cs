using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using My.Functions;

namespace My.Functions.Services;

/// <summary>
/// Drains <see cref="GoogleCalendarWebhookImportQueue"/> on a singleton host
/// lifetime so the HTTP webhook invocation can finish without awaiting Google.
/// </summary>
public sealed class GoogleCalendarWebhookImportWorker(
    GoogleCalendarWebhookImportQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<GoogleCalendarWebhookImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var userId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var importer = scope.ServiceProvider.GetRequiredService<GoogleCalendarFunction>();
                await importer.ImportQueuedWebhookAsync(userId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Queued Google calendar import failed for user {UserId}.", userId);
            }
            finally
            {
                queue.MarkComplete(userId);
            }
        }
    }
}

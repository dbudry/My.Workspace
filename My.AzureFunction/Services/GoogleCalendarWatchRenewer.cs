using Microsoft.Extensions.Logging;
using My.DAL.Models;
using My.DAL.Repository;
using My.Shared.Dtos.GoogleCalendar;
using My.Shared.Rules;

namespace My.Functions.Services;

/// <summary>
/// Re-registers Google Calendar push channels that are expired or due.
/// Shared by the daily timer and Admin → Renew watches.
/// </summary>
public class GoogleCalendarWatchRenewer(
    IRepositoryFactory repositoryFactory,
    GoogleCalendarService google,
    ILogger<GoogleCalendarWatchRenewer> logger)
{
    public const string WebhookUrlEnv = "Google__WebhookUrl";
    public const string WebsiteHostnameEnv = "WEBSITE_HOSTNAME";

    private static readonly TimeSpan TransientRetryDelay = TimeSpan.FromSeconds(5);

    private readonly IRepository<UserSettings> settingsRepository =
        repositoryFactory.GetRepository<UserSettings>();

    public static string? ResolveWebhookUrl() =>
        GoogleCalendarWebhookUrlRules.Resolve(
            Environment.GetEnvironmentVariable(WebhookUrlEnv),
            Environment.GetEnvironmentVariable(WebsiteHostnameEnv));

    public static string WebhookUrlSource() =>
        GoogleCalendarWebhookUrlRules.Source(
            Environment.GetEnvironmentVariable(WebhookUrlEnv),
            Environment.GetEnvironmentVariable(WebsiteHostnameEnv));

    public async Task<GoogleCalendarWatchRenewalResultDto> RenewDueWatchesAsync(CancellationToken cancellationToken)
    {
        var result = new GoogleCalendarWatchRenewalResultDto();
        var webhookUrl = ResolveWebhookUrl();
        if (string.IsNullOrEmpty(webhookUrl))
        {
            logger.LogWarning(
                GoogleCalendarLogEvents.WatchRenewalSkipped,
                "Google calendar watch renewal skipped: no webhook URL (Google__WebhookUrl and WEBSITE_HOSTNAME empty).");
            result.Success = false;
            result.Message = "No webhook URL. Watches cannot be registered. Set Google__WebhookUrl or ensure the Function App has WEBSITE_HOSTNAME.";
            return result;
        }

        var threshold = DateTime.UtcNow.Add(GoogleCalendarWatchRenewalRules.RenewWindow);
        var candidates = (await settingsRepository.Get(s =>
            !string.IsNullOrEmpty(s.GoogleRefreshToken)
            && !string.IsNullOrEmpty(s.GoogleCalendarId)
            && (s.GoogleChannelExpiresAt == null
                || s.GoogleChannelExpiresAt < threshold))).ToList();

        result.Attempted = candidates.Count;
        if (candidates.Count == 0)
        {
            logger.LogWarning(
                GoogleCalendarLogEvents.WatchRenewed,
                "Google calendar watch renewal: no channels due.");
            result.Success = true;
            result.Message = "No watches needed renewal.";
            return result;
        }

        foreach (var settings in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!string.IsNullOrEmpty(settings.GoogleChannelId)
                    && !string.IsNullOrEmpty(settings.GoogleResourceId))
                {
                    try
                    {
                        await google.StopWatchAsync(
                            settings.GoogleRefreshToken!,
                            settings.GoogleChannelId,
                            settings.GoogleResourceId,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Google calendar watch stop of expiring channel {ChannelId} failed; continuing to re-watch.",
                            settings.GoogleChannelId);
                    }
                }

                var newChannelId = Guid.NewGuid().ToString("N");
                var newChannelToken = Guid.NewGuid().ToString("N");
                var ch = await StartWatchWithOneRetryAsync(settings, newChannelId, newChannelToken, webhookUrl, cancellationToken);

                settings.GoogleChannelId = ch.Id ?? newChannelId;
                settings.GoogleChannelToken = newChannelToken;
                settings.GoogleResourceId = ch.ResourceId;
                settings.GoogleChannelExpiresAt = ch.Expiration.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ch.Expiration.Value).UtcDateTime
                    : null;
                await settingsRepository.Update(settings, cancellationToken);

                result.Renewed++;
                logger.LogWarning(
                    GoogleCalendarLogEvents.WatchRenewed,
                    "Google calendar watch renewed for user {UserId}; expires {ExpiresAt}.",
                    settings.UserId, settings.GoogleChannelExpiresAt);
            }
            catch (Exception ex)
            {
                result.Failed++;
                logger.LogError(
                    GoogleCalendarLogEvents.WatchRenewalFailed,
                    ex,
                    "Google calendar watch could not be renewed for user {UserId}.",
                    settings.UserId);
            }
        }

        result.Success = result.Failed == 0;
        result.Message = result.Failed == 0
            ? $"Renewed {result.Renewed} watch(es). Pull missed events for anyone who added tagged time while the watch was expired."
            : $"Renewed {result.Renewed} of {result.Attempted}. {result.Failed} failed — see activity.";
        return result;
    }

    private async Task<Google.Apis.Calendar.v3.Data.Channel> StartWatchWithOneRetryAsync(
        UserSettings settings, string channelId, string channelToken, string webhookUrl, CancellationToken cancellationToken)
    {
        try
        {
            return await google.StartWatchAsync(
                settings.GoogleRefreshToken!, settings.GoogleCalendarId!,
                channelId, channelToken, webhookUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google calendar StartWatch failed for user {UserId}; retrying in {Delay}s.",
                settings.UserId, TransientRetryDelay.TotalSeconds);
            await Task.Delay(TransientRetryDelay, cancellationToken);
            return await google.StartWatchAsync(
                settings.GoogleRefreshToken!, settings.GoogleCalendarId!,
                channelId, channelToken, webhookUrl, cancellationToken);
        }
    }
}

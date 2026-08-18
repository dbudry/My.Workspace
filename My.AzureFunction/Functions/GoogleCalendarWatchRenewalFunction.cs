using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using My.DAL.Models;
using My.DAL.Repository;
using My.Functions.Services;
using My.Shared.Constants;

namespace My.Functions
{
    /// <summary>
    /// Google Calendar push channels expire (~1 week). This timer function re-registers
    /// any channel within the renewal window so inbound sync keeps flowing.
    /// </summary>
    public class GoogleCalendarWatchRenewalFunction
    {
        // Renew anything that expires within 96 hours. Wider than the timer's 24-hour
        // cadence so a single missed/failed run still leaves three more chances to
        // catch the channel before Google drops it.
        private static readonly TimeSpan RenewWindow = TimeSpan.FromHours(96);

        // Single in-loop retry to absorb transient Google API hiccups without waiting
        // a whole day for the next timer fire.
        private static readonly TimeSpan TransientRetryDelay = TimeSpan.FromSeconds(5);

        private readonly IRepository<UserSettings> settingsRepository;
        private readonly GoogleCalendarService google;
        private readonly ILogger<GoogleCalendarWatchRenewalFunction> logger;

        public GoogleCalendarWatchRenewalFunction(
            IRepositoryFactory repositoryFactory,
            GoogleCalendarService google,
            ILogger<GoogleCalendarWatchRenewalFunction> logger)
        {
            settingsRepository = repositoryFactory.GetRepository<UserSettings>();
            this.google = google;
            this.logger = logger;
        }

        // Runs daily at 06:00 UTC
        [Function("RenewGoogleCalendarWatches")]
        public async Task RunAsync([TimerTrigger("0 0 6 * * *")] TimerInfo timer)
        {
            var webhookUrl = Environment.GetEnvironmentVariable("Google__WebhookUrl");
            if (string.IsNullOrEmpty(webhookUrl))
            {
                logger.LogWarning("Google__WebhookUrl not set; cannot renew watches.");
                return;
            }

            var threshold = DateTime.UtcNow.Add(RenewWindow);
            var candidates = await settingsRepository.Get(s =>
                !string.IsNullOrEmpty(s.GoogleRefreshToken)
                && !string.IsNullOrEmpty(s.GoogleCalendarId)
                && s.GoogleChannelExpiresAt != null
                && s.GoogleChannelExpiresAt < threshold);

            foreach (var settings in candidates)
            {
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
                                settings.GoogleResourceId);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Stop of expiring channel {ChannelId} failed; continuing to re-watch.", settings.GoogleChannelId);
                        }
                    }

                    var newChannelId = Guid.NewGuid().ToString("N");
                    var newChannelToken = Guid.NewGuid().ToString("N");
                    var ch = await StartWatchWithOneRetryAsync(settings, newChannelId, newChannelToken, webhookUrl);

                    settings.GoogleChannelId = ch.Id ?? newChannelId;
                    settings.GoogleChannelToken = newChannelToken;
                    settings.GoogleResourceId = ch.ResourceId;
                    settings.GoogleChannelExpiresAt = ch.Expiration.HasValue
                        ? DateTimeOffset.FromUnixTimeMilliseconds(ch.Expiration.Value).UtcDateTime
                        : null;
                    await settingsRepository.Update(settings);

                    logger.LogInformation("Renewed Google Calendar watch for user {UserId}; expires {ExpiresAt}.",
                        settings.UserId, settings.GoogleChannelExpiresAt);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not renew Google Calendar watch for user {UserId}.", settings.UserId);
                }
            }
        }

        private async Task<Google.Apis.Calendar.v3.Data.Channel> StartWatchWithOneRetryAsync(
            UserSettings settings, string channelId, string channelToken, string webhookUrl)
        {
            try
            {
                return await google.StartWatchAsync(
                    settings.GoogleRefreshToken!, settings.GoogleCalendarId!,
                    channelId, channelToken, webhookUrl);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "StartWatch failed for user {UserId}; retrying in {Delay}s.",
                    settings.UserId, TransientRetryDelay.TotalSeconds);
                await Task.Delay(TransientRetryDelay);
                return await google.StartWatchAsync(
                    settings.GoogleRefreshToken!, settings.GoogleCalendarId!,
                    channelId, channelToken, webhookUrl);
            }
        }
    }
}

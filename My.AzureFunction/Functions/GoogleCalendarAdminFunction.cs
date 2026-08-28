using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.DAL.Data;
using My.Functions.Services;
using My.Shared.Constants;
using My.Shared.Dtos.GoogleCalendar;
using My.Shared.Rules;

namespace My.Functions;

/// <summary>
/// Global-Admin status and queue probe for Google Calendar import.
/// </summary>
public class GoogleCalendarAdminFunction(
    ApplicationDbContext dbContext,
    GoogleCalendarImportQueue importQueue,
    GoogleCalendarWatchRenewer watchRenewer,
    IConfiguration configuration,
    ILogger<GoogleCalendarAdminFunction> logger)
{
    [Function("GetGoogleCalendarSyncStatus")]
    public async Task<IActionResult> GetSyncStatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "googlecalendar/syncstatus")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (!Constants.Roles.IsGlobalAdmin(principal))
            return new StatusCodeResult(403);

        var now = DateTime.UtcNow;
        var rows = await (
            from s in dbContext.UserSettings
            join u in dbContext.ApplicationUsers on s.UserId equals u.Id
            where s.GoogleRefreshToken != null && s.GoogleRefreshToken != ""
            orderby u.Email
            select new { s, u.Email, u.FirstName, u.LastName })
            .ToListAsync(req.FunctionContext.CancellationToken);

        var dto = new GoogleCalendarSyncStatusDto
        {
            StorageConfigured = !string.IsNullOrEmpty(configuration["AzureWebJobsStorage"]),
            WebhookUrlReady = !string.IsNullOrEmpty(GoogleCalendarWatchRenewer.ResolveWebhookUrl()),
            WebhookUrlSource = GoogleCalendarWatchRenewer.WebhookUrlSource(),
            Users = rows.Select(r => new GoogleCalendarConnectedUserDto
            {
                UserId = r.s.UserId,
                Email = r.Email ?? "",
                DisplayName = $"{r.FirstName} {r.LastName}".Trim(),
                ImportEnabled = r.s.ImportFromGoogleCalendar,
                PublishEnabled = r.s.PublishToGoogleCalendar,
                WatchExpiresAtUtc = r.s.GoogleChannelExpiresAt,
                Status = GoogleCalendarSyncRules.UserStatus(
                    r.s.ImportFromGoogleCalendar,
                    r.s.GoogleChannelId,
                    r.s.GoogleChannelExpiresAt,
                    now)
            }).ToList()
        };

        return new OkObjectResult(dto);
    }

    [Function("ProbeGoogleCalendarImportQueue")]
    public async Task<IActionResult> ProbeQueueAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "googlecalendar/probequeue")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (!Constants.Roles.IsGlobalAdmin(principal))
            return new StatusCodeResult(403);

        try
        {
            await importQueue.EnqueueAsync(
                new GoogleCalendarImportQueueMessage
                {
                    ChannelId = Constants.API.GoogleCalendar.ProbeChannelId,
                    ChannelToken = "probe",
                    ResourceState = GoogleCalendarWebhookRules.ExistsResourceState
                },
                CancellationToken.None);

            logger.LogWarning(
                GoogleCalendarLogEvents.Enqueued,
                "Google calendar import: admin probe enqueued on the import queue.");

            return new OkObjectResult(new GoogleCalendarQueueProbeDto
            {
                Success = true,
                Message = "Test message written to the import queue. Refresh activity in a few seconds — you should see that the worker is running."
            });
        }
        catch (Exception ex)
        {
            var detail = GoogleCalendarImportQueue.DescribeError(ex);
            logger.LogError(
                GoogleCalendarLogEvents.EnqueueFailed,
                ex,
                "Google calendar import: admin probe failed to enqueue. {Error}",
                detail);
            return new OkObjectResult(new GoogleCalendarQueueProbeDto
            {
                Success = false,
                Message = $"Couldn't write to the import queue. {detail}"
            });
        }
    }

    [Function("RenewGoogleCalendarWatchesNow")]
    public async Task<IActionResult> RenewWatchesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "googlecalendar/renewwatches")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (!Constants.Roles.IsGlobalAdmin(principal))
            return new StatusCodeResult(403);

        var result = await watchRenewer.RenewDueWatchesAsync(req.FunctionContext.CancellationToken);
        return new OkObjectResult(result);
    }
}

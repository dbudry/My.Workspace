using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleCalendarSyncRulesTests
{
    [Fact]
    public void UserStatus_ready_when_import_on_and_watch_in_the_future()
    {
        var now = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        var status = GoogleCalendarSyncRules.UserStatus(true, "ch", now.AddDays(2), now);
        Assert.Equal(GoogleCalendarSyncRules.StatusReady, status);
    }

    [Fact]
    public void UserStatus_watch_expired()
    {
        var now = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        var status = GoogleCalendarSyncRules.UserStatus(true, "ch", now.AddHours(-1), now);
        Assert.Equal(GoogleCalendarSyncRules.StatusWatchExpired, status);
    }

    [Fact]
    public void UserStatus_import_off()
    {
        var status = GoogleCalendarSyncRules.UserStatus(false, "ch", DateTime.UtcNow.AddDays(1), DateTime.UtcNow);
        Assert.Equal(GoogleCalendarSyncRules.StatusImportOff, status);
    }

    [Fact]
    public void ExplainTrace_poison_is_plain_language()
    {
        var text = GoogleCalendarSyncRules.ExplainTrace(
            "Google calendar import is approaching the poison queue. DequeueCount=6");
        Assert.Contains("Pull missed events", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("3113", text);
    }

    [Fact]
    public void ExplainTrace_lease_conflict_is_plain_language()
    {
        var text = GoogleCalendarSyncRules.ExplainTrace(
            "Azure.RequestFailedException: There is currently a lease on the blob and no lease ID was specified in the request. ErrorCode: LeaseIdMissing");
        Assert.Contains("Pull missed events", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplainTrace_unknown_passes_through()
    {
        Assert.Equal("Something else", GoogleCalendarSyncRules.ExplainTrace("Something else"));
    }

    [Fact]
    public void ExplainTrace_watch_renewal_skipped_is_plain_language()
    {
        var text = GoogleCalendarSyncRules.ExplainTrace(
            "Google calendar watch renewal skipped: no webhook URL (Google__WebhookUrl and WEBSITE_HOSTNAME empty).");
        Assert.Contains("webhook URL", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplainTrace_cold_start_timeout_is_plain_language()
    {
        var text = GoogleCalendarSyncRules.ExplainTrace(
            "Google calendar import: admin probe failed to enqueue. Storage timed out or the request was cancelled (often a cold start). Try again.");
        Assert.Contains("waking up", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplainTrace_admin_probe_failed_keeps_storage_guidance()
    {
        var text = GoogleCalendarSyncRules.ExplainTrace(
            "Google calendar import: admin probe failed to enqueue. AuthenticationFailed (403): Server failed to authenticate the request.");
        Assert.Contains("import queue", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplainTrace_admin_probe_enqueued_is_plain_language()
    {
        var text = GoogleCalendarSyncRules.ExplainTrace(
            "Google calendar import: admin probe enqueued on the import queue.");
        Assert.Contains("Test import queue", text, StringComparison.OrdinalIgnoreCase);
    }
}

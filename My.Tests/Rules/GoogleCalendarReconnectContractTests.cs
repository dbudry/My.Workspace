using System.Text.Json;
using My.Shared.Dtos.GoogleCalendar;
using Xunit;
using ApiConstants = My.Shared.Constants.Constants.API;

namespace My.Tests.Rules;

/// <summary>
/// Guards the Resume / forced-consent contract between Settings and
/// <c>GoogleCalendarFunction</c>. The endpoints cannot be run here (no
/// HttpRequestData harness, and <c>GoogleCalendarService</c> is not fakeable),
/// so these pin the wire shape and the ordering that keeps sync safe.
/// </summary>
public class GoogleCalendarReconnectContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Resume_route_constant_matches_the_function_route_literal()
    {
        Assert.Equal("googlecalendar/resume", ApiConstants.GoogleCalendar.Resume);
        var source = File.ReadAllText(FindFunctionSource());
        Assert.Contains("Route = \"googlecalendar/resume\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Callback_body_from_the_client_binds_consentAlreadyForced()
    {
        var json = """{"code":"c","redirectUri":"https://localhost:7047/settings","consentAlreadyForced":true}""";
        var dto = JsonSerializer.Deserialize<GoogleCalendarCallbackDto>(json, Web);

        Assert.NotNull(dto);
        Assert.Equal("c", dto!.Code);
        Assert.True(dto.ConsentAlreadyForced);
    }

    [Fact]
    public void Callback_body_without_the_flag_is_not_forced()
    {
        var dto = JsonSerializer.Deserialize<GoogleCalendarCallbackDto>(
            """{"code":"c","redirectUri":"https://localhost:7047/settings"}""", Web);

        Assert.False(dto!.ConsentAlreadyForced);
    }

    [Fact]
    public void Resume_result_uses_the_property_names_the_client_reads()
    {
        var json = JsonSerializer.Serialize(
            new GoogleCalendarResumeResultDto { NeedsConsent = true, Error = "x" }, Web);
        var back = JsonSerializer.Deserialize<GoogleCalendarResumeResultDto>(json, Web);

        Assert.Contains("\"needsConsent\":true", json, StringComparison.Ordinal);
        Assert.True(back!.NeedsConsent);
        Assert.False(back.Resumed);
        Assert.Equal("x", back.Error);
    }

    [Fact]
    public void Connect_result_carries_NeedsConsent_to_the_client()
    {
        var json = JsonSerializer.Serialize(
            new GoogleCalendarConnectResultDto { Connected = true, NeedsConsent = true }, Web);
        var back = JsonSerializer.Deserialize<GoogleCalendarConnectResultDto>(json, Web);

        Assert.True(back!.NeedsConsent);
        Assert.False(back.SyncNotStarted);
    }

    [Fact]
    public void Resume_saves_reconnect_fields_before_the_watch_and_restores_only_on_a_rejected_token()
    {
        var method = Slice(
            "public async Task<IActionResult> ResumeAsync(",
            "public async Task<IActionResult> CompleteCallbackAsync(");

        var capture = method.IndexOf("CaptureReconnectFields", StringComparison.Ordinal);
        var mark = method.IndexOf("MarkReconnecting", StringComparison.Ordinal);
        var start = method.IndexOf("TryStartWatchAsync", StringComparison.Ordinal);
        var restore = method.IndexOf("RestoreReconnectFields", StringComparison.Ordinal);
        var tokenInvalid = method.IndexOf("WatchStartOutcome.TokenInvalid", start, StringComparison.Ordinal);
        var resumed = method.IndexOf("Resumed = true", StringComparison.Ordinal);

        Assert.True(capture >= 0 && capture < mark, "Capture the prior fields before marking.");
        Assert.True(mark < start, "Mark reconnecting before the watch starts.");
        Assert.True(tokenInvalid > start && restore > tokenInvalid && restore < resumed,
            "Restore only inside the TokenInvalid branch.");
        Assert.Equal(1, Count(method, "RestoreReconnectFields"));
        Assert.Equal(2, Count(method, "NeedsConsent = true")); // no token, or token rejected
    }

    [Fact]
    public void Resume_without_a_stored_token_asks_for_consent_before_touching_settings()
    {
        var method = Slice(
            "public async Task<IActionResult> ResumeAsync(",
            "public async Task<IActionResult> CompleteCallbackAsync(");

        var noToken = method.IndexOf("string.IsNullOrEmpty(settings.GoogleRefreshToken)", StringComparison.Ordinal);
        var firstWrite = method.IndexOf("settingsRepository.Update", StringComparison.Ordinal);

        Assert.True(noToken >= 0 && noToken < firstWrite);
    }

    [Fact]
    public void Callback_passes_the_forced_flag_and_does_not_report_sync_failure_while_consent_is_pending()
    {
        var method = Slice(
            "public async Task<IActionResult> CompleteCallbackAsync(",
            "public async Task<IActionResult> DisconnectAsync(");

        Assert.Contains("consentAlreadyForced: body.ConsentAlreadyForced", method, StringComparison.Ordinal);
        Assert.Contains("SyncNotStarted = syncNotStarted && !needsConsent", method, StringComparison.Ordinal);
        Assert.Contains("NeedsConsent = needsConsent", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Authurl_only_forces_consent_from_the_stored_token_and_the_client_retry_flag()
    {
        var method = Slice(
            "public async Task<IActionResult> GetAuthUrlAsync(",
            "[Function(\"ResumeGoogleCalendarSync\")]");

        Assert.Contains("req.Query[\"forceConsent\"]", method, StringComparison.Ordinal);
        Assert.Contains("GoogleCalendarOAuthRules.ShouldForceConsent(hasToken, tokenRejected)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Disconnect_still_stops_the_watch_and_clears_the_calendar_link()
    {
        var method = Slice(
            "public async Task<IActionResult> DisconnectAsync(",
            "[Function(\"AcknowledgeCalendarBackfillPrompt\")]");

        Assert.Contains("TryStopWatchAsync(settings)", method, StringComparison.Ordinal);
        Assert.Contains("settings.GoogleCalendarId = null", method, StringComparison.Ordinal);
        Assert.Contains("settings.GoogleCalendarAutoConnectOptOut = true", method, StringComparison.Ordinal);
        Assert.DoesNotContain("RevokeAsync", method, StringComparison.Ordinal);
    }

    private static string Slice(string startSignature, string endSignature)
    {
        var source = File.ReadAllText(FindFunctionSource());
        var start = source.IndexOf(startSignature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing {startSignature}");
        var end = source.IndexOf(endSignature, start + startSignature.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing {endSignature}");
        return source[start..end];
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string FindFunctionSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "My.AzureFunction", "Functions", "GoogleCalendarFunction.cs");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("GoogleCalendarFunction.cs was not found above the test output directory.");
    }
}

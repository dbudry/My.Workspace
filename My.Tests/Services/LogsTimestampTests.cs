using My.Client.Pages.Admin;
using Xunit;

namespace My.Tests.Services;

/// <summary>
/// Ensures Admin Logs timestamps from App Insights (UTC / Unspecified) convert
/// to the browser local zone instead of displaying raw UTC wall-clock.
/// </summary>
public class LogsTimestampTests
{
    [Fact]
    public void ToLocal_treats_unspecified_as_utc()
    {
        // 12:35 UTC should not stay 12:35 unless the machine zone is UTC.
        var unspecified = new DateTime(2026, 7, 14, 12, 35, 14, DateTimeKind.Unspecified);
        var local = Logs.ToLocal(unspecified);

        Assert.Equal(DateTimeKind.Local, local.Kind);
        // Round-trip: local → UTC should recover the original instant.
        Assert.Equal(
            new DateTime(2026, 7, 14, 12, 35, 14, DateTimeKind.Utc),
            local.ToUniversalTime());
    }

    [Fact]
    public void ToLocal_preserves_utc_instant()
    {
        var utc = new DateTime(2026, 7, 14, 12, 35, 14, DateTimeKind.Utc);
        var local = Logs.ToLocal(utc);

        Assert.Equal(utc, local.ToUniversalTime());
    }
}

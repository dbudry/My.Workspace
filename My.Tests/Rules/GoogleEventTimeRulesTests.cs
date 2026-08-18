using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Timed entries are UTC in the database. FormatForGoogle converts that instant to
/// wall clock in the user's IANA zone and sends the naked local string + zone.
/// </summary>
public class GoogleEventTimeRulesTests
{
    [Fact]
    public void With_iana_tz_converts_utc_to_wall_clock_and_passes_tz_through()
    {
        // 9:00 AM Eastern (EDT, UTC-4) on 2026-05-15 == 13:00 UTC
        var utc = new DateTime(2026, 5, 15, 13, 0, 0, DateTimeKind.Utc);

        var (raw, tz) = GoogleEventTimeRules.FormatForGoogle(utc, "America/New_York");

        Assert.Equal("2026-05-15T09:00:00", raw);
        Assert.Equal("America/New_York", tz);
    }

    [Fact]
    public void Unspecified_kind_is_treated_as_utc()
    {
        var utcish = new DateTime(2026, 5, 15, 13, 0, 0, DateTimeKind.Unspecified);

        var (raw, _) = GoogleEventTimeRules.FormatForGoogle(utcish, "America/New_York");

        Assert.Equal("2026-05-15T09:00:00", raw);
    }

    [Theory]
    [InlineData("UTC")]
    public void Utc_zone_keeps_wall_clock_matching_instant(string ianaTz)
    {
        var utc = new DateTime(2026, 5, 15, 14, 30, 0, DateTimeKind.Utc);

        var (raw, tz) = GoogleEventTimeRules.FormatForGoogle(utc, ianaTz);

        Assert.Equal("2026-05-15T14:30:00", raw);
        Assert.Equal(ianaTz, tz);
    }

    [Fact]
    public void Wall_clock_string_has_no_offset_suffix()
    {
        // 2026-11-01 05:30 UTC → 01:30 Eastern (EST)
        var utc = new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc);
        var (raw, _) = GoogleEventTimeRules.FormatForGoogle(utc, "America/New_York");

        Assert.DoesNotContain("Z", raw);
        Assert.DoesNotContain("+", raw);
        Assert.Equal("2026-11-01T01:30:00", raw);
    }

    [Fact]
    public void Format_is_culture_invariant()
    {
        var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var utc = new DateTime(2026, 5, 15, 13, 0, 0, DateTimeKind.Utc);
            var (raw, _) = GoogleEventTimeRules.FormatForGoogle(utc, "America/New_York");

            Assert.Equal("2026-05-15T09:00:00", raw);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_tz_falls_back_to_offset_string_with_null_tz(string? missingTz)
    {
        var utc = new DateTime(2026, 5, 15, 9, 0, 0, DateTimeKind.Utc);

        var (raw, tz) = GoogleEventTimeRules.FormatForGoogle(utc, missingTz);

        Assert.Null(tz);
        Assert.Equal("2026-05-15T09:00:00+00:00", raw);
    }

    [Fact]
    public void Midnight_utc_emits_wall_in_zone()
    {
        // 00:00 UTC on May 15 → 20:00 previous evening Eastern (EDT)
        var utc = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc);
        var (raw, _) = GoogleEventTimeRules.FormatForGoogle(utc, "America/New_York");

        Assert.Equal("2026-05-14T20:00:00", raw);
    }

    [Fact]
    public void Sub_minute_precision_is_preserved_to_seconds()
    {
        // 9:07:42 AM Eastern EDT = 13:07:42 UTC
        var utc = new DateTime(2026, 5, 15, 13, 7, 42, DateTimeKind.Utc);
        var (raw, _) = GoogleEventTimeRules.FormatForGoogle(utc, "America/New_York");

        Assert.Equal("2026-05-15T09:07:42", raw);
    }
}

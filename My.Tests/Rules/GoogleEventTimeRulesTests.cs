using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Exercises <see cref="GoogleEventTimeRules.FormatForGoogle"/>. The bug this fixes:
/// a TrackedTask's StartDate is a wall-clock DateTime with Kind=Unspecified (it's the
/// hour the user typed into Tyme). The old <c>BuildEvent</c> stamped Kind=Utc on it
/// and sent the resulting DateTimeOffset to Google, which placed every event at the
/// wrong moment — a 9 AM Eastern task surfaced at 5 AM Eastern. The new behavior is
/// to send a wall-clock string + the user's IANA zone and let Google interpret it.
/// </summary>
public class GoogleEventTimeRulesTests
{
    // ---------- Happy path: real user tz produces wall-clock + tz ----------

    [Fact]
    public void With_iana_tz_emits_wall_clock_string_and_passes_tz_through()
    {
        var local = new DateTime(2026, 5, 15, 9, 0, 0);

        var (raw, tz) = GoogleEventTimeRules.FormatForGoogle(local, "America/New_York");

        Assert.Equal("2026-05-15T09:00:00", raw);
        Assert.Equal("America/New_York", tz);
    }

    [Theory]
    [InlineData("America/Los_Angeles")]
    [InlineData("Europe/London")]
    [InlineData("Asia/Tokyo")]
    [InlineData("UTC")]
    public void Any_non_empty_tz_is_forwarded_verbatim(string ianaTz)
    {
        var local = new DateTime(2026, 5, 15, 14, 30, 0);

        var (raw, tz) = GoogleEventTimeRules.FormatForGoogle(local, ianaTz);

        Assert.Equal("2026-05-15T14:30:00", raw);
        Assert.Equal(ianaTz, tz);
    }

    // ---------- Wall-clock string does NOT bake in DST or offset ----------
    //
    // The whole point of letting Google interpret the moment is that we never have to
    // think about DST. The output string must be a naked local time with no Z and no
    // ±HH:MM suffix. If we ever regress and append an offset, Google will use the
    // offset and ignore our timeZone field — re-introducing the original bug.

    [Fact]
    public void Wall_clock_string_has_no_offset_suffix()
    {
        var local = new DateTime(2026, 11, 1, 1, 30, 0); // Eastern DST end overlap
        var (raw, _) = GoogleEventTimeRules.FormatForGoogle(local, "America/New_York");

        Assert.DoesNotContain("Z", raw);
        Assert.DoesNotContain("+", raw);
        // Two negative signs in the date portion are fine; just ensure there's no offset.
        Assert.Equal("2026-11-01T01:30:00", raw);
    }

    [Fact]
    public void Format_is_culture_invariant()
    {
        // Some cultures format DateTime with non-ISO separators or different month order;
        // if we ever forget to pass CultureInfo.InvariantCulture, Google will reject the
        // payload silently and we'll lose another afternoon.
        var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var (raw, _) = GoogleEventTimeRules.FormatForGoogle(
                new DateTime(2026, 5, 15, 9, 0, 0), "America/New_York");

            Assert.Equal("2026-05-15T09:00:00", raw);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    // ---------- Null/empty tz falls back to UTC-stamped offset string ----------
    //
    // This is the "user hasn't set a timezone yet" path. We do the legacy thing
    // (stamp as UTC, emit with +00:00 offset) so the API call doesn't fail. The
    // result is only correct if the moment really was UTC, but that's the same
    // contract we had before; this rule exists so the fix doesn't break installs
    // that haven't populated UserSettings.TimeZone.

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_tz_falls_back_to_offset_string_with_null_tz(string? missingTz)
    {
        var local = new DateTime(2026, 5, 15, 9, 0, 0);

        var (raw, tz) = GoogleEventTimeRules.FormatForGoogle(local, missingTz);

        Assert.Null(tz);
        Assert.Equal("2026-05-15T09:00:00+00:00", raw);
    }

    // ---------- Midnight and second-precision edge cases ----------

    [Fact]
    public void Midnight_emits_zero_padded_time()
    {
        var (raw, _) = GoogleEventTimeRules.FormatForGoogle(
            new DateTime(2026, 5, 15, 0, 0, 0), "America/New_York");

        Assert.Equal("2026-05-15T00:00:00", raw);
    }

    [Fact]
    public void Sub_minute_precision_is_preserved_to_seconds()
    {
        var (raw, _) = GoogleEventTimeRules.FormatForGoogle(
            new DateTime(2026, 5, 15, 9, 7, 42), "America/New_York");

        Assert.Equal("2026-05-15T09:07:42", raw);
    }
}

using My.Client.Helpers;
using Xunit;

namespace My.Tests.Helpers;

public class DateTimeWireTests
{
    private static readonly TimeZoneInfo Eastern =
        TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");

    [Fact]
    public void ToUserTime_converts_utc_to_configured_zone_wall_clock()
    {
        // 2026-01-15 is outside DST, so EST = UTC-5.
        var utc = new DateTime(2026, 1, 15, 14, 0, 0, DateTimeKind.Utc);
        var local = DateTimeWire.ToUserTime(utc, Eastern);

        Assert.Equal(new DateTime(2026, 1, 15, 9, 0, 0), local);
        Assert.Equal(DateTimeKind.Unspecified, local.Kind);
    }

    [Fact]
    public void ToUserTime_treats_unspecified_kind_as_utc()
    {
        // EF/JSON often strip Kind on the wire; Unspecified must be handled like Utc,
        // not silently reinterpreted as already-local (which would double-shift it).
        var unspecified = new DateTime(2026, 1, 15, 14, 0, 0, DateTimeKind.Unspecified);
        var local = DateTimeWire.ToUserTime(unspecified, Eastern);

        Assert.Equal(new DateTime(2026, 1, 15, 9, 0, 0), local);
    }

    [Fact]
    public void ToUserTime_handles_daylight_saving_offset_change()
    {
        // 2026-07-15 is inside DST, so EDT = UTC-4 (not the -5 used in winter).
        var utc = new DateTime(2026, 7, 15, 14, 0, 0, DateTimeKind.Utc);
        var local = DateTimeWire.ToUserTime(utc, Eastern);

        Assert.Equal(new DateTime(2026, 7, 15, 10, 0, 0), local);
    }

    [Fact]
    public void ToUserTime_nullable_overload_passes_through_null()
    {
        Assert.Null(DateTimeWire.ToUserTime((DateTime?)null, Eastern));
    }

    [Fact]
    public void ToUtc_treats_input_as_wall_clock_in_the_given_zone()
    {
        var wallClock = new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Unspecified);
        var utc = DateTimeWire.ToUtc(wallClock, Eastern);

        Assert.Equal(new DateTime(2026, 1, 15, 14, 0, 0), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void ToUtc_already_utc_kind_passes_through_unchanged()
    {
        var utc = new DateTime(2026, 1, 15, 14, 0, 0, DateTimeKind.Utc);
        Assert.Equal(utc, DateTimeWire.ToUtc(utc, Eastern));
    }

    [Fact]
    public void ToUserTime_then_ToUtc_round_trips()
    {
        var original = new DateTime(2026, 3, 1, 12, 34, 0, DateTimeKind.Utc);
        var roundTripped = DateTimeWire.ToUtc(DateTimeWire.ToUserTime(original, Eastern), Eastern);

        Assert.Equal(original, roundTripped);
    }
}

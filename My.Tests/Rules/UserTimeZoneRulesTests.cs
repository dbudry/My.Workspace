using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class UserTimeZoneRulesTests
{
    [Fact]
    public void GetSelectableTimeZoneIds_includes_iana_ids_on_windows()
    {
        var zones = UserTimeZoneRules.GetSelectableTimeZoneIds();

        Assert.Contains(zones, z => z.Contains('/', StringComparison.Ordinal));
    }

    [Fact]
    public void Resolve_finds_iana_id()
    {
        var tz = UserTimeZoneRules.Resolve("America/New_York");

        Assert.True(
            tz.Id.Equals("America/New_York", StringComparison.OrdinalIgnoreCase)
            || tz.Id.Equals("Eastern Standard Time", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_falls_back_to_local_when_unknown()
    {
        var tz = UserTimeZoneRules.Resolve("Not/A_Real_Zone");

        Assert.Equal(TimeZoneInfo.Local.Id, tz.Id);
    }
}
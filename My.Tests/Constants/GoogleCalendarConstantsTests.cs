using My.Shared.Constants;
using Xunit;

namespace My.Tests.ConstantsTests;

/// <summary>
/// Pins the URL shape of the "pull missed events from Google" endpoint. The
/// client side (Settings page + admin UserManager) and the server route share
/// these constants, so a typo or accidental rename here would silently 404 on
/// production. Cheap to test, expensive to debug.
/// </summary>
public class GoogleCalendarConstantsTests
{
    [Fact]
    public void Route_constant_matches_expected_path()
    {
        Assert.Equal("googlecalendar/pullfromgoogle", Constants.API.GoogleCalendar.PullFromGoogle);
    }

    [Fact]
    public void Construct_includes_from_and_to_in_iso_date_format()
    {
        var url = Constants.API.GoogleCalendar.ConstructPullFromGoogle(
            new DateTime(2026, 1, 5),
            new DateTime(2026, 2, 14));

        Assert.Equal("googlecalendar/pullfromgoogle?from=2026-01-05&to=2026-02-14", url);
    }

    [Fact]
    public void Construct_omits_userId_when_caller_is_self()
    {
        var url = Constants.API.GoogleCalendar.ConstructPullFromGoogle(
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 7));

        Assert.DoesNotContain("userId", url);
    }

    [Fact]
    public void Construct_appends_userId_when_admin_targets_another_user()
    {
        var url = Constants.API.GoogleCalendar.ConstructPullFromGoogle(
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 7),
            targetUserId: "abc-123");

        Assert.Contains("userId=abc-123", url);
    }

    [Fact]
    public void Construct_url_encodes_userId_to_protect_against_injection()
    {
        // A user id with regex/URL specials would otherwise break the parsed query
        // and could be misread by the server. Uri.EscapeDataString in the helper
        // catches both. Pin the encoding so a future "simplify" doesn't drop it.
        var url = Constants.API.GoogleCalendar.ConstructPullFromGoogle(
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 1),
            targetUserId: "weird id&hack=1");

        Assert.Contains("userId=weird%20id%26hack%3D1", url);
    }

    [Fact]
    public void Construct_null_or_empty_userId_does_not_add_param()
    {
        var nullUser = Constants.API.GoogleCalendar.ConstructPullFromGoogle(
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 1), targetUserId: null);
        var emptyUser = Constants.API.GoogleCalendar.ConstructPullFromGoogle(
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 1), targetUserId: "");

        Assert.DoesNotContain("userId", nullUser);
        Assert.DoesNotContain("userId", emptyUser);
    }
}

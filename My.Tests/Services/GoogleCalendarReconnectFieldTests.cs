using My.DAL.Models;
using My.Functions;
using Xunit;

namespace My.Tests.Services;

public class GoogleCalendarReconnectFieldTests
{
    [Fact]
    public void TokenInvalid_restore_puts_back_the_fields_resume_saved()
    {
        var settings = new UserSettings
        {
            GoogleCalendarId = null,
            PublishToGoogleCalendar = false,
            ImportFromGoogleCalendar = false,
            GoogleCalendarAutoConnectOptOut = true
        };

        var prior = GoogleCalendarFunction.CaptureReconnectFields(settings);
        GoogleCalendarFunction.MarkReconnecting(settings);

        Assert.Equal("primary", settings.GoogleCalendarId);
        Assert.True(settings.PublishToGoogleCalendar);
        Assert.True(settings.ImportFromGoogleCalendar);
        Assert.False(settings.GoogleCalendarAutoConnectOptOut);

        GoogleCalendarFunction.RestoreReconnectFields(settings, prior);

        Assert.Null(settings.GoogleCalendarId);
        Assert.False(settings.PublishToGoogleCalendar);
        Assert.False(settings.ImportFromGoogleCalendar);
        Assert.True(settings.GoogleCalendarAutoConnectOptOut);
    }
}

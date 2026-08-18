namespace My.Functions.Helpers;

/// <summary>
/// Generic client-facing error text. Details belong in server logs only.
/// </summary>
public static class ApiErrorMessages
{
    public const string GenericBadRequest =
        "The request could not be completed. Check application logs for details.";

    public const string GenericServerError =
        "An unexpected error occurred. Please try again later.";

    public const string GoogleCalendarConnectFailed =
        "Could not complete Google Calendar connection. Please try again.";

    public const string GoogleCalendarListFailed =
        "Could not list events from Google. Try a narrower date range or try again later.";

    public const string DriveOperationFailed =
        "A Google Drive operation failed. Please try again.";

    public const string LogQueryFailed =
        "Log query failed. Check application logs for details.";
}
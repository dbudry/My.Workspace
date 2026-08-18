using System.Net;
using System.Net.Http;
using System.Text.Json;
using MudBlazor;

namespace My.Client.Extensions;

public static class SnackbarExtensions
{
    /// <summary>
    /// Translates an exception from a failed API call into a user-friendly snackbar
    /// message. Skips 401 (handled separately by UnauthorizedDelegatingHandler) and
    /// strips cryptic JSON parse messages that surface when the server returns an
    /// HTML error page instead of JSON.
    /// </summary>
    /// <param name="contextMessage">Short prefix describing what was being attempted,
    /// e.g. "Couldn't load dashboard data."</param>
    public static void AddApiError(this ISnackbar snackbar, Exception ex, string contextMessage)
    {
        var friendly = ex switch
        {
            // 401 is already handled (and snackbar'd) by UnauthorizedDelegatingHandler.
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => null,

            // 5xx after retries — almost always a backend issue or warm-up failure.
            HttpRequestException httpEx when (int?)httpEx.StatusCode >= 500 =>
                $"{contextMessage} The server may be unavailable — try again in a moment.",

            // Other HTTP errors (4xx).
            HttpRequestException httpEx =>
                $"{contextMessage} ({(int?)httpEx.StatusCode} {httpEx.StatusCode})",

            // Server returned non-JSON (often an HTML error page).
            JsonException =>
                $"{contextMessage} Got an unexpected response from the server.",

            // Request timed out before completing.
            TaskCanceledException =>
                $"{contextMessage} The request timed out.",

            // Don't echo blank/whitespace messages back to the user.
            _ when string.IsNullOrWhiteSpace(ex.Message) =>
                contextMessage,

            // Non-HTTP exceptions may contain internal details — keep the context only.
            _ => contextMessage
        };

        if (friendly is not null)
        {
            snackbar.Add(friendly, Severity.Error);
        }
    }
}

using System.Net;
using System.Net.Http;

namespace My.Shared.Rules;

/// <summary>
/// Decides whether an HTTP request that just returned a non-success status
/// is safe to retry. Pure function — no I/O, no state.
///
/// Why a helper: the retry policy used to be a single hashset of status codes
/// in <c>RetryDelegatingHandler</c> that applied to every method. That broke
/// the Google Calendar OAuth callback: a slow first attempt caused the client
/// to retry the POST, by which time Google had already consumed the auth code,
/// so the retry returned 400 invalid_grant and the user saw a failure even
/// though the original POST eventually completed. The fix is to gate retries
/// on HTTP method semantics — POST/PATCH are not idempotent and must not be
/// auto-retried on 5xx, because we can't tell whether the server already
/// applied the side effects.
/// </summary>
public static class RetryPolicy
{
    /// <summary>
    /// Returns true if a response with this status code on this method is
    /// safe to retry without risking double side effects.
    ///
    /// Retry rules:
    /// <list type="bullet">
    ///   <item><b>Idempotent methods</b> (GET, HEAD, OPTIONS, PUT, DELETE):
    ///     retry on 500/502/503/504. The server may apply the same operation
    ///     twice; that's the definition of idempotent.</item>
    ///   <item><b>Non-idempotent methods</b> (POST, PATCH): retry only on
    ///     502/503/504 — these indicate the gateway never reached the function
    ///     or the function was unavailable, so no side effects could have run.
    ///     Do NOT retry on 500: the function may have partially executed
    ///     (consumed an OAuth code, sent an email, written a row) and the
    ///     retry would either double-apply or fail with a now-stale precondition.</item>
    /// </list>
    /// </summary>
    public static bool ShouldRetry(HttpMethod method, HttpStatusCode status)
    {
        if (!IsRetryableStatus(status)) return false;

        // 502/503/504 are gateway-level failures — server-side code never ran,
        // so retry is safe regardless of method.
        if (status != HttpStatusCode.InternalServerError) return true;

        // 500: server-side code ran and threw. Only safe to retry if the method
        // itself is idempotent.
        return IsIdempotent(method);
    }

    private static bool IsRetryableStatus(HttpStatusCode status) =>
        status is HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get
        || method == HttpMethod.Head
        || method == HttpMethod.Options
        || method == HttpMethod.Put
        || method == HttpMethod.Delete;
}

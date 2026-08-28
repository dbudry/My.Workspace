namespace My.Shared.Rules;

/// <summary>
/// Plain-language storage errors for Admin → Debug → Google Calendar.
/// Azure exception types stay in the Function project; this formats the pieces.
/// </summary>
public static class GoogleCalendarStorageErrorRules
{
    public static bool IsTransientStatus(int status) =>
        status is 0 or 408 or 429 or 500 or 503;

    /// <summary>
    /// Creating the lock blob when it already exists, including when another
    /// worker holds a lease (Azure returns 412 LeaseIdMissing instead of 409).
    /// </summary>
    public static bool IsLockBlobAlreadyPresent(int status, string? errorCode) =>
        status is 409 or 412
        || string.Equals(errorCode, "BlobAlreadyExists", StringComparison.OrdinalIgnoreCase)
        || string.Equals(errorCode, "LeaseIdMissing", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Acquire/renew should wait: someone else holds the lease.
    /// </summary>
    public static bool IsLeaseHeld(int status, string? errorCode) =>
        status is 409 or 412
        || string.Equals(errorCode, "LeaseAlreadyPresent", StringComparison.OrdinalIgnoreCase)
        || string.Equals(errorCode, "LeaseIdMissing", StringComparison.OrdinalIgnoreCase);

    public static string Format(int? status, string? errorCode, string? message)
    {
        var body = string.IsNullOrWhiteSpace(message) ? "Storage call failed." : message.Trim();
        var code = string.IsNullOrWhiteSpace(errorCode) ? null : errorCode.Trim();
        if (status is > 0 && code != null)
            return $"{code} ({status}): {body}";
        if (status is > 0)
            return $"HTTP {status}: {body}";
        if (code != null)
            return $"{code}: {body}";
        return body;
    }
}

namespace My.Shared.Rules;

/// <summary>
/// Scopes and extra query params for Settings → Connect Google Calendar.
/// Calendar Connect must not request Drive: <c>drive.readonly</c> is a
/// restricted Google scope, and combining it with Calendar (especially with
/// <c>login_hint</c> + <c>prompt=consent</c>) makes accounts.google.com return
/// "Backend Error" instead of an authorization code.
/// </summary>
public static class GoogleCalendarOAuthRules
{
    public const string CalendarScope = "https://www.googleapis.com/auth/calendar";
    public const string EmailScope = "https://www.googleapis.com/auth/userinfo.email";
    public const string OpenIdScope = "openid";

    public static readonly string[] ConnectScopes =
    [
        CalendarScope,
        EmailScope,
        OpenIdScope
    ];

    public static bool IsDriveScope(string? scope) =>
        !string.IsNullOrWhiteSpace(scope)
        && scope.Contains("googleapis.com/auth/drive", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Calendar Connect is Calendar-only. A new refresh token from that flow
    /// would drop Drive from the stored grant. Keep the existing token when
    /// Intranet Drive is already granted (Google may still return a Calendar-only
    /// refresh token). Empty incoming tokens never overwrite.
    /// </summary>
    public static bool ShouldOverwriteCalendarRefreshToken(
        string? incomingRefreshToken, bool hasExistingToken, bool driveGranted)
    {
        if (string.IsNullOrEmpty(incomingRefreshToken))
            return false;
        if (driveGranted && hasExistingToken)
            return false;
        return true;
    }

    /// <summary>
    /// After a StartWatch failure, retry with the freshly-received token only when we chose
    /// to keep an old one <em>and</em> Google actually gave us a new token to try. Otherwise
    /// there is nothing to retry with (empty incoming token), or nothing was kept in the
    /// first place (the token we tried was already the new one).
    /// </summary>
    public static bool ShouldRetryWatchWithFreshToken(
        bool watchStartFailed, bool keptExistingToken, string? incomingRefreshToken) =>
        watchStartFailed && keptExistingToken && !string.IsNullOrEmpty(incomingRefreshToken);

    /// <summary>
    /// Only an OAuth-level "this refresh token is dead" response justifies replacing a kept,
    /// Drive-capable token with a Calendar-only one. A transient network blip or a Google 5xx
    /// must not cost the user their Drive grant — retrying later with the same token would
    /// likely have worked fine.
    /// </summary>
    public static bool IsRevokedTokenError(string? oauthErrorCode) =>
        string.Equals(oauthErrorCode, "invalid_grant", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Optionally locks the consent screen to a single hosted domain when the tenant
    /// policy has exactly one domain (see <see cref="GoogleIdentityRules.GetSingleHostedDomainHint"/>).
    /// Do not also append <c>login_hint</c>: that plus <c>prompt=consent</c>
    /// is a known Google consent 500 for some Workspace users.
    /// </summary>
    public static string AppendHostedDomainHint(string authorizationUrl, string? hostedDomain)
    {
        if (string.IsNullOrWhiteSpace(authorizationUrl))
            throw new ArgumentException("Authorization URL is required.", nameof(authorizationUrl));

        if (string.IsNullOrWhiteSpace(hostedDomain))
            return authorizationUrl;

        var sep = authorizationUrl.Contains('?') ? "&" : "?";
        return $"{authorizationUrl}{sep}hd={Uri.EscapeDataString(hostedDomain.Trim())}";
    }
}

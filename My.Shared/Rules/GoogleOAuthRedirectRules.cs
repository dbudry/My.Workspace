namespace My.Shared.Rules;

/// <summary>
/// Allowlist for Google OAuth redirect URIs used by Calendar/Drive connect flows.
/// Must match paths registered in Google Cloud Console (typically …/settings).
/// </summary>
public static class GoogleOAuthRedirectRules
{
    public const string RequiredPath = "/settings";

    private static readonly HashSet<string> AllowedRedirectUris = new(StringComparer.OrdinalIgnoreCase)
    {
        "https://localhost:7047/settings",
        "https://your-app.example.com/settings",
        "https://zealous-grass-01d92eb0f.7.azurestaticapps.net/settings",
    };

    /// <summary>
    /// Adds <c>redirectUri</c> to an auth-url route. A route that already has a query
    /// (<c>?forceConsent=true</c>) must use <c>&amp;</c>.
    /// </summary>
    public static string AppendRedirectUri(string route, string redirectUri)
    {
        if (string.IsNullOrWhiteSpace(route))
            throw new ArgumentException("Auth URL route is required.", nameof(route));
        if (string.IsNullOrWhiteSpace(redirectUri))
            throw new ArgumentException("Redirect URI is required.", nameof(redirectUri));

        var separator = route.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{route}{separator}redirectUri={Uri.EscapeDataString(redirectUri)}";
    }

    public static bool IsAllowedRedirectUri(string? redirectUri, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            error = "redirectUri is required.";
            return false;
        }

        var trimmed = redirectUri.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "redirectUri must be an absolute https URL.";
            return false;
        }

        if (!uri.AbsolutePath.Equals(RequiredPath, StringComparison.OrdinalIgnoreCase))
        {
            error = $"redirectUri must end with {RequiredPath}.";
            return false;
        }

        if (string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
            && AllowedRedirectUris.Contains(trimmed))
        {
            return true;
        }

        if (string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
            && uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && uri.Port > 0)
        {
            return true;
        }

        foreach (var extra in GetExtraAllowedFromEnvironment())
        {
            if (string.Equals(trimmed, extra, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        error = "redirectUri is not an allowed OAuth callback URL.";
        return false;
    }

    private static IEnumerable<string> GetExtraAllowedFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("Google__AllowedOAuthRedirectUris");
        if (string.IsNullOrWhiteSpace(raw))
            yield break;

        foreach (var segment in raw.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = segment.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                yield return trimmed;
        }
    }
}
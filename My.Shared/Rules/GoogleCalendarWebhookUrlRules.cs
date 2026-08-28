using ConstantsClass = My.Shared.Constants.Constants;

namespace My.Shared.Rules;

/// <summary>
/// Public HTTPS address Google POSTs calendar push notifications to.
/// Connect has an HTTP request; the daily renewal timer does not.
/// </summary>
public static class GoogleCalendarWebhookUrlRules
{
    public const string SourceAppSetting = "App setting";
    public const string SourceFunctionHost = "Function host";
    public const string SourceMissing = "Missing";

    /// <summary>
    /// Timer / Admin renew: app setting, else Azure Functions <c>WEBSITE_HOSTNAME</c>.
    /// </summary>
    public static string? Resolve(string? configured, string? websiteHostname)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();
        return FromHost(websiteHostname, "https");
    }

    /// <summary>
    /// Connect / HTTP: app setting, else the incoming request host, else Function host.
    /// </summary>
    public static string? ResolveFromHttp(
        string? configured,
        string? requestHost,
        string? requestScheme,
        string? websiteHostname)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();
        var scheme = string.IsNullOrWhiteSpace(requestScheme) ? "https" : requestScheme.Trim();
        var fromRequest = FromHost(requestHost, scheme);
        if (fromRequest != null)
            return fromRequest;
        return FromHost(websiteHostname, "https");
    }

    public static string Source(string? configured, string? websiteHostname)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return SourceAppSetting;
        if (!string.IsNullOrWhiteSpace(websiteHostname))
            return SourceFunctionHost;
        return SourceMissing;
    }

    public static string? FromHost(string? host, string scheme)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var h = host.Trim().TrimEnd('/');
        if (h.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || h.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return $"{h}/api/{ConstantsClass.API.GoogleCalendar.Webhook}";

        return $"{scheme}://{h}/api/{ConstantsClass.API.GoogleCalendar.Webhook}";
    }
}

namespace My.Shared.Rules;

/// <summary>
/// Loopback hosts used for local debugging. Production Static Web Apps / Functions
/// hosts never match these — the LOCAL bar must stay off in deployed environments.
/// </summary>
public static class LocalEnvironmentRules
{
    /// <summary>
    /// Blazor client ports from launchSettings / Dev-Start (HTTPS 7047, HTTP 5047, extra 11256).
    /// The LOCAL bar must not appear on 443 / default HTTPS (production).
    /// </summary>
    public static readonly string[] LocalClientPorts = ["7047", "5047", "11256"];

    public static bool IsLocalHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var h = host.Trim();
        if (h.StartsWith('[') && h.IndexOf(']') is > 0 and var end)
            h = h[1..end];
        else
        {
            var colon = h.IndexOf(':');
            if (colon >= 0 && h.IndexOf(':', colon + 1) < 0)
                h = h[..colon];
        }

        return h.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || h.Equals("127.0.0.1", StringComparison.Ordinal)
            || h.Equals("::1", StringComparison.Ordinal);
    }

    public static bool IsKnownProductionHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var h = host.Trim();
        var colon = h.IndexOf(':');
        if (colon >= 0 && h.IndexOf(':', colon + 1) < 0 && !h.StartsWith('['))
            h = h[..colon];

        return h.EndsWith(".azurestaticapps.net", StringComparison.OrdinalIgnoreCase)
            || h.EndsWith(".azurewebsites.net", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True only for the local debug client (loopback + Dev-Start ports).
    /// Production hosts never match, even if a proxy made the hostname look local.
    /// </summary>
    public static bool IsLocalDebugClient(string? host, string? port)
    {
        if (IsKnownProductionHost(host))
            return false;
        if (!IsLocalHost(host))
            return false;

        var p = port?.Trim() ?? string.Empty;
        if (p.Length == 0 && host is not null)
        {
            var h = host.Trim();
            var colon = h.LastIndexOf(':');
            if (colon >= 0 && int.TryParse(h[(colon + 1)..], out _))
                p = h[(colon + 1)..];
        }

        return LocalClientPorts.Contains(p, StringComparer.Ordinal);
    }

    public static bool IsLocalDebugClient(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
            return false;
        var port = uri.IsDefaultPort ? string.Empty : uri.Port.ToString();
        return IsLocalDebugClient(uri.Host, port);
    }
}

using System.Text;

namespace My.Shared.Rules;

/// <summary>
/// Resolves the workspace service-account JSON used to write the Team Availability
/// calendar. Azure App Settings often mangle PEM newlines, so the value may be
/// raw JSON or standard base64 of that JSON.
/// </summary>
public static class GoogleTeamAvailabilityCredentialRules
{
    public const string JsonEnvVar = "Google__TeamAvailabilityServiceAccountJson";
    public const string ImpersonateUserEnvVar = "Google__TeamAvailabilityImpersonateUser";

    public static bool IsConfigured(string? raw) => TryResolveJson(raw, out _);

    public static bool TryResolveJson(string? raw, out string json)
    {
        json = "";
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        if (LooksLikeServiceAccountJson(trimmed))
        {
            json = trimmed;
            return true;
        }

        var compact = StripWhitespace(trimmed);
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(compact)).Trim();
            if (!LooksLikeServiceAccountJson(decoded))
                return false;
            json = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string? NormalizeImpersonateUser(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    private static bool LooksLikeServiceAccountJson(string value) =>
        value.StartsWith('{')
        && value.Contains("service_account", StringComparison.OrdinalIgnoreCase);

    private static string StripWhitespace(string value)
    {
        var buffer = new char[value.Length];
        var n = 0;
        foreach (var c in value)
        {
            if (!char.IsWhiteSpace(c))
                buffer[n++] = c;
        }
        return new string(buffer, 0, n);
    }
}

namespace My.Shared.Rules;

/// <summary>
/// Public site links in email. The host comes from configuration so a local or
/// other environment does not point at production.
/// </summary>
public static class ExpensePublicLinkRules
{
    public const string ProductionBaseUrl = "https://app.example.com";

    public static string ReportUrl(string? configuredBaseUrl, string reportId)
    {
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? ProductionBaseUrl
            : configuredBaseUrl.Trim().TrimEnd('/');
        return $"{baseUrl}/expenses/{reportId}";
    }
}

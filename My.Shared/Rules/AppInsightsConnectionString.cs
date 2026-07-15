namespace My.Shared.Rules;

/// <summary>
/// Pulls fields out of the standard App Insights connection string Azure sets as
/// <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> when AI is wired up. The string is a
/// semicolon-delimited list of key=value pairs, e.g.:
/// <c>InstrumentationKey=...;IngestionEndpoint=...;LiveEndpoint=...;ApplicationId=&lt;guid&gt;</c>.
/// Lives in My.Shared.Rules so it's testable without dragging in the function host.
/// </summary>
public static class AppInsightsConnectionString
{
    public static string? GetApplicationId(string? connectionString) =>
        GetField(connectionString, "ApplicationId");

    private static string? GetField(string? connectionString, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0 || eq == part.Length - 1) continue;

            var key = part.AsSpan(0, eq).Trim();
            if (key.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                return part.AsSpan(eq + 1).Trim().ToString();
        }

        return null;
    }
}

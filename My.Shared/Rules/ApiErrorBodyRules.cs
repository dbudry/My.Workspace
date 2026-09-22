using System.Text.Json;

namespace My.Shared.Rules;

/// <summary>
/// Turns an API error body into a single snackbar string. FluentValidation failures
/// come back as a JSON string array; other endpoints return a plain or quoted string.
/// </summary>
public static class ApiErrorBodyRules
{
    public static string Format(string? body, string fallback)
    {
        if (string.IsNullOrWhiteSpace(body))
            return fallback;

        var trimmed = body.Trim();
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                var parts = root.EnumerateArray()
                    .Select(ReadString)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                if (parts.Count > 0)
                    return string.Join(" ", parts!);
            }
            else if (root.ValueKind == JsonValueKind.String)
            {
                var s = root.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through to the raw body.
        }

        var unquoted = trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
        return string.IsNullOrWhiteSpace(unquoted) ? fallback : unquoted;
    }

    private static string? ReadString(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString();
}

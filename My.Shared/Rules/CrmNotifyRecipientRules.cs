using System.Text.Json;

namespace My.Shared.Rules;

/// <summary>
/// The CRM manager picks which users get an email when a discussion is posted.
/// The setting stores user ids as a JSON array. No setting means nobody is emailed.
/// </summary>
public static class CrmNotifyRecipientRules
{
    public const int UserIdMaxLength = 450;

    public static IReadOnlyList<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(json);
            return Normalize(ids);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string Serialize(IEnumerable<string?>? userIds) =>
        JsonSerializer.Serialize(Normalize(userIds));

    /// <summary>Trims, drops blanks, removes duplicates, and sorts so saves are stable.</summary>
    public static List<string> Normalize(IEnumerable<string?>? userIds) =>
        (userIds ?? [])
            .Select(id => id?.Trim())
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
}

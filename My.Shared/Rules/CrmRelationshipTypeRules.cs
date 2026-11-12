using System.Text.Json;

namespace My.Shared.Rules;

public sealed record CrmRelationshipType(string Name, string Description);

/// <summary>
/// Relationship types live on the contact, once per person. Decision Maker stays in the list.
/// </summary>
public static class CrmRelationshipTypeRules
{
    public const string DecisionMaker = "Decision Maker";
    public const int NameMaxLength = 50;
    public const int DescriptionMaxLength = 200;

    public const string DefaultJson =
        """[{"Name":"Decision Maker","Description":"the person who can approve the purchase"},{"Name":"Influencer","Description":"a person who shapes the decision and does not give final approval"},{"Name":"Assessor","Description":"a person who evaluates fit, risk, or terms"}]""";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<CrmRelationshipType> Defaults => Parse(DefaultJson);

    public static IReadOnlyList<CrmRelationshipType> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Parse(DefaultJson);

        try
        {
            var parsed = JsonSerializer.Deserialize<List<CrmRelationshipTypeDtoShape>>(json, ReadOptions);
            if (parsed is null || parsed.Count == 0)
                return Parse(DefaultJson);

            var list = new List<CrmRelationshipType>();
            foreach (var item in parsed)
            {
                var name = item.Name?.Trim();
                if (string.IsNullOrEmpty(name))
                    continue;
                if (list.Any(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                list.Add(new CrmRelationshipType(name, item.Description?.Trim() ?? ""));
            }

            return list.Count == 0 ? Parse(DefaultJson) : list;
        }
        catch (JsonException)
        {
            return Parse(DefaultJson);
        }
    }

    public static string Serialize(IEnumerable<CrmRelationshipType> types) =>
        JsonSerializer.Serialize(types.Select(type => new CrmRelationshipTypeDtoShape
        {
            Name = type.Name,
            Description = type.Description
        }));

    public static bool IsAllowed(string? name, IReadOnlyList<CrmRelationshipType> allowed)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        return allowed.Any(type => string.Equals(type.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Blank becomes null. A match returns the stored name. An unknown name returns null.</summary>
    public static string? Canonical(string? name, IReadOnlyList<CrmRelationshipType> allowed)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return allowed.FirstOrDefault(type =>
            string.Equals(type.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))?.Name;
    }

    public static string? ValidateUpdate(
        IReadOnlyList<CrmRelationshipType> proposed,
        IReadOnlyDictionary<string, int> usageByName)
    {
        if (proposed.Count == 0)
            return "At least one relationship type is required.";

        var names = new List<string>();
        foreach (var type in proposed)
        {
            var name = type.Name?.Trim() ?? "";
            if (name.Length == 0)
                return "Relationship type name is required.";
            if (name.Length > NameMaxLength)
                return $"\"{name}\" can't exceed {NameMaxLength} characters.";
            var description = type.Description?.Trim() ?? "";
            if (description.Length > DescriptionMaxLength)
                return $"The description for \"{name}\" can't exceed {DescriptionMaxLength} characters.";
            if (names.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
                return $"\"{name}\" is listed more than once.";
            names.Add(name);
        }

        if (!names.Any(name => string.Equals(name, DecisionMaker, StringComparison.OrdinalIgnoreCase)))
            return $"\"{DecisionMaker}\" must remain in the list.";

        foreach (var pair in usageByName)
        {
            if (pair.Value <= 0 || string.IsNullOrWhiteSpace(pair.Key))
                continue;
            if (names.Any(name => string.Equals(name, pair.Key, StringComparison.OrdinalIgnoreCase)))
                continue;
            return pair.Value == 1
                ? $"Cannot remove \"{pair.Key}\": 1 contact still uses it. Reassign that contact first."
                : $"Cannot remove \"{pair.Key}\": {pair.Value} contacts still use it. Reassign those contacts first.";
        }

        return null;
    }

    private sealed class CrmRelationshipTypeDtoShape
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
    }
}

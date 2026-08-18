using System.Text.Json;

namespace My.Shared.Rules
{
    public static class ContactTypeRules
    {
        public const string Primary = "Primary";
        public const string Billing = "Billing";
        public const string Maintenance = "Maintenance";

        public static readonly IReadOnlyList<string> DefaultTypes = new[]
        {
            Primary,
            Billing,
            Maintenance
        };

        public static string DefaultTypesJson => JsonSerializer.Serialize(DefaultTypes);

        public static IReadOnlyList<string> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return DefaultTypes;

            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(json);
                if (parsed is null || parsed.Count == 0)
                    return DefaultTypes;

                return parsed
                    .Select(t => t?.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(t => t!)
                    .ToList();
            }
            catch
            {
                return DefaultTypes;
            }
        }

        public static string Serialize(IEnumerable<string> types)
        {
            var normalized = types
                .Select(t => t?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(t => t!)
                .ToList();

            return JsonSerializer.Serialize(normalized.Count > 0 ? normalized : DefaultTypes);
        }

        public static bool IsAllowed(string? contactType, IReadOnlyList<string> allowedTypes)
        {
            if (string.IsNullOrWhiteSpace(contactType))
                return false;

            return allowedTypes.Any(t => string.Equals(t, contactType.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static string Normalize(string contactType, IReadOnlyList<string> allowedTypes)
        {
            var match = allowedTypes.FirstOrDefault(t =>
                string.Equals(t, contactType.Trim(), StringComparison.OrdinalIgnoreCase));

            return match ?? contactType.Trim();
        }

        public static string DefaultForManualEntry(IReadOnlyList<string> allowedTypes)
        {
            var primary = allowedTypes.FirstOrDefault(t =>
                string.Equals(t, Primary, StringComparison.OrdinalIgnoreCase));
            return primary ?? allowedTypes[0];
        }

        public static bool IncludesPrimary(IReadOnlyList<string> types) =>
            types.Any(t => string.Equals(t, Primary, StringComparison.OrdinalIgnoreCase));

        public static IReadOnlyList<string> GetRemovedTypes(
            IReadOnlyList<string> oldTypes,
            IReadOnlyList<string> newTypes) =>
            oldTypes
                .Where(old => !newTypes.Any(n => string.Equals(n, old, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        public static int GetUsageCount(string type, IReadOnlyDictionary<string, int> usageByType)
        {
            if (string.IsNullOrWhiteSpace(type))
                return 0;

            var total = 0;
            foreach (var pair in usageByType)
            {
                if (string.Equals(pair.Key, type, StringComparison.OrdinalIgnoreCase))
                    total += pair.Value;
            }

            return total;
        }

        /// <summary>
        /// Returns an error message when a contact-type settings change is not allowed; null when valid.
        /// </summary>
        public static string? ValidateSettingsUpdate(
            IReadOnlyList<string> oldTypes,
            IReadOnlyList<string> newTypes,
            IReadOnlyDictionary<string, int> usageByType)
        {
            if (newTypes.Count == 0)
                return "At least one contact type is required.";

            if (!IncludesPrimary(newTypes))
                return $"\"{Primary}\" must remain in the list — it is the default for new contacts.";

            foreach (var removed in GetRemovedTypes(oldTypes, newTypes))
            {
                var count = GetUsageCount(removed, usageByType);
                if (count > 0)
                {
                    return count == 1
                        ? $"Cannot remove \"{removed}\": 1 contact still uses this type. Change that contact's type first."
                        : $"Cannot remove \"{removed}\": {count} contacts still use this type. Change their types first.";
                }
            }

            return null;
        }
    }
}
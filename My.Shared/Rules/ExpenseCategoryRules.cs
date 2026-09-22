namespace My.Shared.Rules;

/// <summary>
/// Closed category and Form 87-43 letter-code lists. Keep these as strings (not a
/// reorderable enum) so a later QuickBooks account map can key off stable values.
/// </summary>
public static class ExpenseCategoryRules
{
    public const string Mileage = "Mileage";
    public const string Transportation = "Transportation";
    public const string Hotel = "Hotel";
    public const string Meals = "Meals";
    public const string Entertainment = "Entertainment";
    public const string Telephone = "Telephone";
    public const string Miscellaneous = "Miscellaneous";
    public const string Software = "Software";

    public static readonly IReadOnlyList<string> All =
    [
        Mileage,
        Transportation,
        Hotel,
        Meals,
        Entertainment,
        Telephone,
        Miscellaneous,
        Software
    ];

    public const string PersonalCarCode = "M";

    public static readonly IReadOnlyList<(string Value, string Label)> CategoryChoices =
    [
        (Transportation, "Transportation"),
        (Hotel, "Hotel"),
        (Meals, "Meals"),
        (Entertainment, "Entertainment"),
        (Telephone, "Telephone"),
        (Miscellaneous, "Miscellaneous"),
        (Software, "Software / Subscription")
    ];

    public static readonly IReadOnlyList<(string Value, string Label)> TransportationCodes =
    [
        (PersonalCarCode, "Personal car"),
        ("A", "A — Rented car"),
        ("B", "B — Plane ticket"),
        ("C", "C — Local fares"),
        ("F", "F — Tolls"),
        ("P", "P — Parking"),
        ("R", "R — Rail ticket"),
        ("T", "T — Airport tax"),
        ("W", "W — Gas"),
        ("X", "X — Excess weight"),
        ("Y", "Y — Other")
    ];

    public static readonly IReadOnlyList<(string Value, string Label)> MiscellaneousCodes =
    [
        ("G", "G — Tips"),
        ("H", "H — Postage"),
        ("L", "L — Laundry"),
        ("V", "V — Valet"),
        ("Z", "Z — Other")
    ];

    public static bool IsKnown(string? category) =>
        !string.IsNullOrWhiteSpace(category)
        && All.Contains(category.Trim(), StringComparer.Ordinal);

    /// <summary>
    /// Transportation (including personal-car mileage), miscellaneous, and meals have extra
    /// line fields. Software, telephone, hotel, and entertainment do not.
    /// </summary>
    public static bool HasLineDetails(string? category)
    {
        var value = Normalize(category);
        return value is Mileage or Transportation or Miscellaneous or Meals;
    }

    public static bool IsPersonalCar(string? category, string? transportationCode)
    {
        var value = Normalize(category);
        if (string.Equals(value, Mileage, StringComparison.Ordinal))
            return true;
        return string.Equals(value, Transportation, StringComparison.Ordinal)
            && string.Equals(NormalizeCode(transportationCode), PersonalCarCode, StringComparison.Ordinal);
    }

    /// <summary>Maps a legacy Mileage category row onto Transportation + personal-car code.</summary>
    public static (string Category, string? TransportationCode) ForStorage(string? category, string? transportationCode)
    {
        var value = Normalize(category);
        if (string.Equals(value, Mileage, StringComparison.Ordinal))
            return (Transportation, PersonalCarCode);
        return (value, string.IsNullOrWhiteSpace(transportationCode) ? null : NormalizeCode(transportationCode));
    }

    public static bool IsTransportationCode(string? code) =>
        !string.IsNullOrWhiteSpace(code)
        && TransportationCodes.Any(c => string.Equals(c.Value, code.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsMiscellaneousCode(string? code) =>
        !string.IsNullOrWhiteSpace(code)
        && MiscellaneousCodes.Any(c => string.Equals(c.Value, code.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? category)
    {
        var trimmed = category?.Trim() ?? "";
        return All.FirstOrDefault(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase))
               ?? trimmed;
    }

    public static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return code.Trim().ToUpperInvariant();
    }
}

using System.Globalization;

namespace My.Shared.Rules;

/// <summary>
/// Personal-car mileage rate stored in App Settings (<c>ExpensesMileageRatePerMile</c>).
/// Matches the Form 87-43 allowance (0.555 USD/mile in the current working spreadsheet).
/// </summary>
public static class ExpenseMileageRateRules
{
    public const decimal DefaultPerMile = 0.555m;
    public const decimal MinPerMile = 0m;
    public const decimal MaxPerMile = 10m;

    public static decimal Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DefaultPerMile;
        if (!decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return DefaultPerMile;
        if (value < MinPerMile || value > MaxPerMile)
            return DefaultPerMile;
        return value;
    }

    public static string ToStorage(decimal value)
    {
        var clamped = value < MinPerMile ? MinPerMile : value > MaxPerMile ? MaxPerMile : value;
        return clamped.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

using System.Globalization;

namespace My.Shared.Rules;

/// <summary>
/// Duration cells in CSV exports. The screen shows <c>02:30</c>, but Excel reads a quoted
/// <c>"02:30"</c> as a time of day (or text past 24 hours), so it cannot sum or pivot it.
/// Exports write decimal hours as an unquoted number instead.
/// </summary>
public static class DurationCsvRules
{
    /// <summary>Decimal hours with two places and a period, for example <c>2.50</c>.</summary>
    public static string ToDecimalHours(double totalSeconds)
    {
        if (double.IsNaN(totalSeconds) || double.IsInfinity(totalSeconds) || totalSeconds < 0)
            totalSeconds = 0;

        var hours = Math.Round(totalSeconds / 3600d, 2, MidpointRounding.AwayFromZero);
        return hours.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

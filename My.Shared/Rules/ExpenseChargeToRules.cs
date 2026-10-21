using System.Globalization;

namespace My.Shared.Rules;

public static class ExpenseChargeToRules
{
    /// <summary>Query value for the lines that have no Charge to, when a report has more than one form.</summary>
    public const string UnassignedQuery = "unassigned";

    public static string Key(string? organizationId) =>
        string.IsNullOrWhiteSpace(organizationId) ? "" : organizationId.Trim();

    /// <summary>
    /// A blank query means the only form. <see cref="UnassignedQuery"/> selects the lines with no Charge to.
    /// </summary>
    public static (bool Specified, string Key) ReadQuery(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (false, "");
        var trimmed = raw.Trim();
        var key = string.Equals(trimmed, UnassignedQuery, StringComparison.OrdinalIgnoreCase)
            ? ""
            : trimmed;
        return (true, key);
    }

    public static List<string> MissingReceiptLabels(
        IEnumerable<(DateTime Date, string? Description, decimal Amount, int ReceiptCount)> lines)
    {
        return lines
            .Where(line => line.ReceiptCount <= 0)
            .Select(line =>
            {
                var what = string.IsNullOrWhiteSpace(line.Description) ? "Untitled line" : line.Description.Trim();
                return $"{line.Date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)}  {what}  {line.Amount.ToString("C", CultureInfo.GetCultureInfo("en-US"))}";
            })
            .ToList();
    }

    public static List<ExpenseChargeGroup<T>> Group<T>(
        IEnumerable<T> lines,
        Func<T, string?> organizationId,
        Func<T, string?> name,
        string fallbackName)
    {
        return lines
            .GroupBy(line => Key(organizationId(line)), StringComparer.Ordinal)
            .Select(group =>
            {
                var named = group
                    .Select(name)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                return new ExpenseChargeGroup<T>
                {
                    Key = group.Key,
                    Name = string.IsNullOrWhiteSpace(named) ? fallbackName : named.Trim(),
                    Lines = group.ToList()
                };
            })
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class ExpenseChargeGroup<T>
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public List<T> Lines { get; init; } = [];
}

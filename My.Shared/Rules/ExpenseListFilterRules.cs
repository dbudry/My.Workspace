namespace My.Shared.Rules;

public static class ExpenseListFilterRules
{
    public static (int Year, int Month) PriorMonth(DateTime today)
    {
        var prior = today.AddMonths(-1);
        return (prior.Year, prior.Month);
    }

    public static bool MatchesPeriod(
        int year,
        int month,
        IReadOnlyCollection<int> years,
        IReadOnlyCollection<int> months)
    {
        if (years.Count > 0 && !years.Contains(year))
            return false;
        if (months.Count > 0 && !months.Contains(month))
            return false;
        return true;
    }

    public static (int Year, int Month) CreatePeriod(
        IReadOnlyCollection<int> years,
        IReadOnlyCollection<int> months,
        DateTime today)
    {
        if (years.Count == 1 && months.Count == 1)
            return (years.First(), months.First());
        return (today.Year, today.Month);
    }

    /// <summary>
    /// Years offered on My expenses: only years that already have a report.
    /// </summary>
    public static List<int> YearChoices(IEnumerable<int> yearsFromRows) =>
        yearsFromRows
            .Where(y => y is >= 2000 and <= 9999)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

    public static List<int> ParseInts(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var result = new List<int>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var n))
                result.Add(n);
        }

        return result.Distinct().ToList();
    }
}

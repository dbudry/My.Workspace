using System.Globalization;
using System.Text;
using My.Shared.Dtos.Expenses;

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

    /// <summary>
    /// A chosen year keeps a report whose cover period includes that year, from the start
    /// year through the end year. A filed report also stays when the year is the year it
    /// was submitted. No year means show every report.
    /// </summary>
    public static bool MatchesListYear(
        string? status,
        int submitYear,
        DateTime coverStart,
        DateTime coverEnd,
        int? year)
    {
        if (year is null)
            return true;
        if (TryCoverYearSpan(coverStart, coverEnd, out var startYear, out var endYear)
            && year.Value >= startYear
            && year.Value <= endYear)
            return true;
        if (ExpenseStatusRules.IsLocked(status))
            return submitYear == year.Value;
        return coverStart == default && coverEnd == default;
    }

    /// <summary>
    /// Years to offer for one report: each year the cover period touches, plus the submit
    /// year once the report is filed. Drafts with no cover dates contribute nothing.
    /// </summary>
    public static IEnumerable<int> YearsOnReport(
        string? status,
        int submitYear,
        DateTime coverStart,
        DateTime coverEnd)
    {
        if (TryCoverYearSpan(coverStart, coverEnd, out var startYear, out var endYear))
        {
            for (var year = startYear; year <= endYear; year++)
                yield return year;
        }

        if (ExpenseStatusRules.IsLocked(status))
            yield return submitYear;
    }

    private static bool TryCoverYearSpan(
        DateTime coverStart,
        DateTime coverEnd,
        out int startYear,
        out int endYear)
    {
        startYear = 0;
        endYear = 0;
        if (coverStart == default && coverEnd == default)
            return false;

        var start = coverStart == default ? coverEnd : coverStart;
        var end = coverEnd == default ? coverStart : coverEnd;
        if (end.Year < start.Year)
            (start, end) = (end, start);
        startYear = start.Year;
        endYear = end.Year;
        return true;
    }

    /// <summary>
    /// Year/month chips filter the submit month. Drafts are not in a month yet, so they stay visible.
    /// </summary>
    public static bool MatchesFilingMonth(
        string? status,
        int year,
        int month,
        IReadOnlyCollection<int> years,
        IReadOnlyCollection<int> months)
    {
        if (!ExpenseStatusRules.IsLocked(status))
            return true;
        return MatchesPeriod(year, month, years, months);
    }

    /// <summary>
    /// No selected statuses means every status. Otherwise the report status must be one of them.
    /// </summary>
    public static bool MatchesStatuses(string? status, IReadOnlyCollection<string>? selected)
    {
        if (selected == null || selected.Count == 0)
            return true;
        return selected.Any(choice => string.Equals(choice, status, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Blank search matches every report. Otherwise the text must appear on the report
    /// (name, status, cover dates, total) or on a line (description, category, amount, date, receipt name).
    /// </summary>
    public static bool MatchesSearch(ExpenseReportListDto row, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var needle = query.Trim().Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal);
        if (needle.Length == 0)
            return true;

        return SearchHaystack(row).Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static string SearchHaystack(ExpenseReportListDto row)
    {
        var text = new StringBuilder();
        Append(text, row.EmployeeName);
        Append(text, row.Status);
        Append(text, row.Purpose);
        Append(text, row.PlantOrLocation);
        Append(text, row.ChargeToNote);
        Append(text, ExpenseReportRules.CoverRangeLabel(row.CoverStart, row.CoverEnd));
        AppendDate(text, row.CoverStart);
        AppendDate(text, row.CoverEnd);
        AppendAmount(text, row.TotalAmount);
        Append(text, row.LineCount.ToString(CultureInfo.InvariantCulture));

        foreach (var line in row.Lines)
        {
            Append(text, line.Description);
            Append(text, line.Category);
            Append(text, ChoiceLabel(ExpenseCategoryRules.CategoryChoices, line.Category));
            AppendDate(text, line.Date);
            AppendAmount(text, line.Amount);
            if (line.Miles is decimal miles)
                AppendAmount(text, miles);
            Append(text, line.TransportationCode);
            Append(text, ChoiceLabel(ExpenseCategoryRules.TransportationCodes, line.TransportationCode));
            Append(text, line.MiscellaneousCode);
            Append(text, ChoiceLabel(ExpenseCategoryRules.MiscellaneousCodes, line.MiscellaneousCode));
            if (line.MealBreakfast) Append(text, "Breakfast");
            if (line.MealLunch) Append(text, "Lunch");
            if (line.MealDinner) Append(text, "Dinner");
            foreach (var name in line.ReceiptFileNames)
                Append(text, name);
        }

        return text.ToString();
    }

    private static void Append(StringBuilder text, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        text.Append(' ').Append(value.Trim());
    }

    private static void AppendDate(StringBuilder text, DateTime date)
    {
        if (date == default)
            return;
        text.Append(' ').Append(date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture));
        text.Append(' ').Append(date.ToString("M/d/yyyy", CultureInfo.InvariantCulture));
        text.Append(' ').Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static void AppendAmount(StringBuilder text, decimal amount)
    {
        text.Append(' ').Append(amount.ToString("0.00", CultureInfo.InvariantCulture));
        text.Append(' ').Append(amount.ToString("0.##", CultureInfo.InvariantCulture));
    }

    private static string? ChoiceLabel(IReadOnlyList<(string Value, string Label)> choices, string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        foreach (var choice in choices)
        {
            if (string.Equals(choice.Value, code.Trim(), StringComparison.OrdinalIgnoreCase))
                return choice.Label;
        }

        return null;
    }

    /// <summary>
    /// Valid years, newest first. Callers pass the years on the reports being listed.
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

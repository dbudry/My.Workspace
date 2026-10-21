namespace My.Shared.Rules;

/// <summary>
/// Cover and yellow totals for one expense. Gross is every line's US dollar amount,
/// including a line that was later returned. Credit is the part of that person's
/// same-Charge-to bucket that landed on this expense. Net is gross minus that credit.
/// </summary>
public static class ExpenseReportMoneyRules
{
    public static ExpenseReportMoney Summarize(
        IReadOnlyList<ExpenseMoneyLine> reportLines,
        IReadOnlyDictionary<string, decimal>? appliedByLine,
        bool creditsEnabled,
        decimal formCreditUsd = 0m,
        string? homeCode = null)
    {
        var gross = reportLines.Sum(line => line.AmountUsd);
        decimal credit = 0m;
        if (creditsEnabled && appliedByLine != null)
        {
            foreach (var line in reportLines)
            {
                if (appliedByLine.TryGetValue(line.LineId, out var part))
                    credit += part;
            }
        }

        if (creditsEnabled && formCreditUsd > 0m)
            credit += formCreditUsd;

        if (credit > gross)
            credit = gross;

        var created = creditsEnabled
            ? reportLines.Where(line => line.IsReturned).Sum(line => line.AmountUsd)
            : 0m;

        var currencies = reportLines
            .GroupBy(line => ExpenseCurrencyRules.NormalizeCode(line.CurrencyCode), StringComparer.Ordinal)
            .Select(group => new ExpenseCurrencyTotal
            {
                CurrencyCode = group.Key,
                Color = group.Select(line => line.Color).FirstOrDefault(color => !string.IsNullOrWhiteSpace(color)),
                SpentAmount = group.Sum(line => line.SpentAmount),
                UsdAmount = group.Sum(line => line.AmountUsd)
            })
            .OrderBy(row => ExpenseCurrencyRules.IsHome(row.CurrencyCode, homeCode) ? 0 : 1)
            .ThenBy(row => row.CurrencyCode, StringComparer.Ordinal)
            .ToList();

        return new ExpenseReportMoney
        {
            GrossUsd = gross,
            CreditUsd = credit,
            NetUsd = gross - credit,
            CreditCreatedUsd = created,
            Currencies = currencies
        };
    }

    public static decimal NetForReport(
        string reportId,
        IReadOnlyList<ExpenseCreditSource> sources,
        IReadOnlyList<ExpenseCreditLine> lines,
        bool creditsEnabled,
        decimal formCreditUsd = 0m,
        string? homeCode = null)
    {
        var mine = lines.Where(line => string.Equals(line.ReportId, reportId, StringComparison.Ordinal)).ToList();
        var gross = mine.Sum(line => line.AmountUsd);
        if (!creditsEnabled)
            return gross;

        var applied = ExpenseCreditRules.AppliedByLine(sources, lines, homeCode);
        decimal credit = 0m;
        foreach (var line in mine)
        {
            if (applied.TryGetValue(line.LineId, out var part))
                credit += part;
        }

        if (formCreditUsd > 0m)
            credit += formCreditUsd;
        if (credit > gross)
            credit = gross;
        return gross - credit;
    }
}

public sealed class ExpenseMoneyLine
{
    public string LineId { get; init; } = "";
    public string CurrencyCode { get; init; } = ExpenseCurrencyRules.UsdCode;
    public string? Color { get; init; }
    public decimal SpentAmount { get; init; }
    public decimal AmountUsd { get; init; }
    public bool IsReturned { get; init; }
}

public sealed class ExpenseCurrencyTotal
{
    public string CurrencyCode { get; init; } = ExpenseCurrencyRules.UsdCode;
    public string? Color { get; init; }
    public decimal SpentAmount { get; init; }
    public decimal UsdAmount { get; init; }
}

public sealed class ExpenseReportMoney
{
    public decimal GrossUsd { get; init; }
    public decimal CreditUsd { get; init; }
    public decimal NetUsd { get; init; }
    public decimal CreditCreatedUsd { get; init; }
    public IReadOnlyList<ExpenseCurrencyTotal> Currencies { get; init; } = [];
}

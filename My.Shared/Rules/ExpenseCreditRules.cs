namespace My.Shared.Rules;

/// <summary>
/// A returned paid line becomes a credit for that person and that Charge to.
/// It reduces the person's unpaid expenses with the same Charge to, oldest line
/// date first, until it is used up. The click date is stored on the credit.
/// It does not decide which expenses can use the credit.
/// </summary>
public static class ExpenseCreditRules
{
    public static IReadOnlyDictionary<string, decimal> AppliedByLine(
        IEnumerable<ExpenseCreditSource> sources,
        IEnumerable<ExpenseCreditLine> lines,
        string? homeCode = null) =>
        Allocate(sources, lines, homeCode).Applied;

    /// <summary>How much of each credit, in the company default currency, landed on a later expense.</summary>
    public static IReadOnlyDictionary<string, decimal> ConsumedBySource(
        IEnumerable<ExpenseCreditSource> sources,
        IEnumerable<ExpenseCreditLine> lines,
        string? homeCode = null) =>
        Allocate(sources, lines, homeCode).Consumed;

    public static bool HasBeenUsed(
        string? sourceLineId,
        IEnumerable<ExpenseCreditSource> sources,
        IEnumerable<ExpenseCreditLine> lines,
        string? homeCode = null)
    {
        if (string.IsNullOrWhiteSpace(sourceLineId))
            return false;
        var filed = lines.Where(line => ExpenseStatusRules.IsSubmitted(line.ReportStatus));
        return ConsumedBySource(sources, filed, homeCode).TryGetValue(sourceLineId, out var used) && used > 0m;
    }

    private static (Dictionary<string, decimal> Applied, Dictionary<string, decimal> Consumed) Allocate(
        IEnumerable<ExpenseCreditSource> sources,
        IEnumerable<ExpenseCreditLine> lines,
        string? homeCode)
    {
        var remaining = lines
            .Select(line => (Line: line, Money: Read(line.CurrencyCode, line.SpentAmount, line.ExchangeRateToUsd, line.AmountUsd, homeCode)))
            .Where(row => row.Line.CanReceiveCredit && row.Money.Usd > 0m)
            .OrderBy(row => row.Line.Date)
            .ThenBy(row => row.Line.SortOrder)
            .ThenBy(row => row.Line.LineId, StringComparer.Ordinal)
            .Select(row => new Bucket(row.Line, row.Money))
            .ToList();

        var applied = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var consumed = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var source in sources
                     .Select(source => (Source: source, Money: Read(source.CurrencyCode, source.SpentAmount, source.ExchangeRateToUsd, source.AmountUsd, homeCode)))
                     .Where(row => row.Money.Usd > 0m || row.Money.Spent > 0m)
                     .OrderBy(row => row.Source.ReturnDate)
                     .ThenBy(row => row.Source.LineId, StringComparer.Ordinal))
        {
            var spentLeft = source.Money.Spent;
            for (var i = 0; i < remaining.Count && spentLeft > 0m; i++)
            {
                var row = remaining[i];
                if (row.UsdLeft <= 0m)
                    continue;
                if (!SameChargeTo(row.Line.ChargeToOrganizationId, source.Source.ChargeToOrganizationId))
                    continue;
                if (string.Equals(row.Line.LineId, source.Source.LineId, StringComparison.Ordinal))
                    continue;

                decimal usdTake;
                if (SameCurrency(source.Money.Code, row.Code))
                {
                    if (row.SpentLeft <= 0m)
                        continue;
                    var takeSpent = spentLeft < row.SpentLeft ? spentLeft : row.SpentLeft;
                    usdTake = takeSpent == row.SpentLeft
                        ? row.UsdLeft
                        : ExpenseCurrencyRules.ToUsd(takeSpent, row.Rate);
                    if (usdTake > row.UsdLeft)
                        usdTake = row.UsdLeft;
                    if (usdTake <= 0m)
                        continue;
                    row.SpentLeft -= takeSpent;
                    spentLeft -= takeSpent;
                }
                else
                {
                    var creditUsd = ExpenseCurrencyRules.ToUsd(spentLeft, source.Money.Rate);
                    if (creditUsd <= 0m)
                        break;
                    usdTake = creditUsd < row.UsdLeft ? creditUsd : row.UsdLeft;
                    if (usdTake <= 0m)
                        continue;
                    if (usdTake >= creditUsd)
                    {
                        spentLeft = 0m;
                    }
                    else
                    {
                        var spentCost = source.Money.Rate <= 0m
                            ? spentLeft
                            : decimal.Round(usdTake / source.Money.Rate, 2, MidpointRounding.AwayFromZero);
                        spentLeft = spentCost >= spentLeft || spentCost <= 0m ? 0m : spentLeft - spentCost;
                    }

                    if (row.Rate > 0m && usdTake < row.UsdLeft)
                    {
                        var spentLeftOnLine = decimal.Round(row.UsdLeft - usdTake, 2, MidpointRounding.AwayFromZero);
                        var nextSpent = decimal.Round(spentLeftOnLine / row.Rate, 2, MidpointRounding.AwayFromZero);
                        if (nextSpent < row.SpentLeft)
                            row.SpentLeft = nextSpent;
                    }
                }

                row.UsdLeft -= usdTake;
                if (row.UsdLeft <= 0m)
                {
                    row.UsdLeft = 0m;
                    row.SpentLeft = 0m;
                }

                applied.TryGetValue(row.Line.LineId, out var already);
                applied[row.Line.LineId] = already + usdTake;
                consumed.TryGetValue(source.Source.LineId, out var used);
                consumed[source.Source.LineId] = used + usdTake;
            }
        }

        return (applied, consumed);
    }

    private static Money Read(string? currencyCode, decimal spentAmount, decimal exchangeRateToUsd, decimal amountUsd, string? homeCode)
    {
        var code = ExpenseCurrencyRules.NormalizeCode(currencyCode);
        var rate = exchangeRateToUsd <= 0m ? 1m : exchangeRateToUsd;
        if (ExpenseCurrencyRules.IsHome(code, homeCode))
            rate = 1m;
        var spent = spentAmount;
        var usd = amountUsd;
        if (spent <= 0m && usd > 0m)
            spent = ExpenseCurrencyRules.IsUsd(code)
                ? usd
                : decimal.Round(usd / rate, 2, MidpointRounding.AwayFromZero);
        if (usd <= 0m && spent > 0m)
            usd = ExpenseCurrencyRules.ToUsd(spent, rate);
        return new Money(code, spent < 0m ? 0m : spent, rate, usd < 0m ? 0m : usd);
    }

    private static bool SameCurrency(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private readonly record struct Money(string Code, decimal Spent, decimal Rate, decimal Usd);

    private sealed class Bucket
    {
        public Bucket(ExpenseCreditLine line, Money money)
        {
            Line = line;
            Code = money.Code;
            Rate = money.Rate;
            SpentLeft = money.Spent;
            UsdLeft = money.Usd;
        }

        public ExpenseCreditLine Line { get; }
        public string Code { get; }
        public decimal Rate { get; }
        public decimal SpentLeft { get; set; }
        public decimal UsdLeft { get; set; }
    }

    public static bool CanReceiveCredit(string? reportStatus) =>
        ExpenseStatusRules.IsDraft(reportStatus) || ExpenseStatusRules.IsSubmitted(reportStatus);

    public static bool SameChargeTo(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        return string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
    }
}

public sealed class ExpenseCreditSource
{
    public string LineId { get; init; } = "";
    public string ReportId { get; init; } = "";
    public string? ChargeToOrganizationId { get; init; }
    public DateTime ReturnDate { get; init; }
    public string CurrencyCode { get; init; } = ExpenseCurrencyRules.UsdCode;
    public decimal SpentAmount { get; init; }
    public decimal ExchangeRateToUsd { get; init; } = 1m;
    public decimal AmountUsd { get; init; }
}

public sealed class ExpenseCreditLine
{
    public string LineId { get; init; } = "";
    public string ReportId { get; init; } = "";
    public string? ChargeToOrganizationId { get; init; }
    public string? ReportStatus { get; init; }
    public DateTime Date { get; init; }
    public string CurrencyCode { get; init; } = ExpenseCurrencyRules.UsdCode;
    public decimal SpentAmount { get; init; }
    public decimal ExchangeRateToUsd { get; init; } = 1m;
    public decimal AmountUsd { get; init; }
    public int SortOrder { get; init; }
    public bool IsReturned { get; init; }

    public bool CanReceiveCredit =>
        !IsReturned && ExpenseCreditRules.CanReceiveCredit(ReportStatus);
}

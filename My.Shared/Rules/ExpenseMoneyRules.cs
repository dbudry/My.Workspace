namespace My.Shared.Rules;

/// <summary>
/// Turns the amount a person spent into the US dollar amount stored on the line.
/// Personal-car mileage stays miles times the report mileage rate, in USD.
/// A saved line keeps its exchange rate until the currency on that line changes.
/// </summary>
public static class ExpenseMoneyRules
{
    public readonly record struct LineMoney(
        string CurrencyCode,
        decimal ExchangeRateToUsd,
        decimal SpentAmount,
        decimal AmountUsd);

    public static LineMoney Apply(
        string category,
        string? transportationCode,
        decimal? miles,
        decimal mileageRatePerMile,
        decimal spentAmount,
        string? currencyCode,
        decimal catalogRate,
        decimal? savedRate,
        string? savedCurrency,
        string? homeCode = null,
        bool userEntersRate = false,
        decimal? enteredRate = null)
    {
        if (ExpenseCategoryRules.IsPersonalCar(category, transportationCode))
        {
            var usd = ExpenseLineRules.ComputeAmount(
                category, miles, 0m, mileageRatePerMile, transportationCode);
            return new LineMoney(ExpenseCurrencyRules.UsdCode, 1m, usd, usd);
        }

        var code = ExpenseCurrencyRules.NormalizeCode(currencyCode);
        var spent = decimal.Round(spentAmount < 0m ? 0m : spentAmount, 2, MidpointRounding.AwayFromZero);
        if (ExpenseCurrencyRules.IsHome(code, homeCode))
            return new LineMoney(code, 1m, spent, spent);

        if (userEntersRate)
        {
            var posted = enteredRate.GetValueOrDefault();
            if (posted <= 0m)
                return new LineMoney(code, 0m, spent, 0m);
            var entered = ExpenseCurrencyRules.NormalizeRate(code, posted, homeCode);
            return new LineMoney(code, entered, spent, ExpenseCurrencyRules.ToUsd(spent, entered));
        }

        var rate = SnapshotRate(code, savedRate, savedCurrency, catalogRate, homeCode);
        var amount = ExpenseCurrencyRules.ToUsd(spent, rate);
        return new LineMoney(code, rate, spent, amount);
    }

    /// <summary>
    /// The amount the person typed. A US dollar line that never stored a spent
    /// amount falls back to its dollar amount. A euro line stays in euros.
    /// </summary>
    public static decimal EnteredAmount(decimal spentAmount, decimal amountUsd, string? currencyCode)
    {
        if (spentAmount == 0m && amountUsd != 0m && ExpenseCurrencyRules.IsUsd(currencyCode))
            return amountUsd;
        return spentAmount < 0m ? 0m : spentAmount;
    }

    public static decimal SnapshotRate(
        string? currencyCode,
        decimal? savedRate,
        string? savedCurrency,
        decimal catalogRate,
        string? homeCode = null)
    {
        var code = ExpenseCurrencyRules.NormalizeCode(currencyCode);
        if (ExpenseCurrencyRules.IsHome(code, homeCode))
            return 1m;
        if (savedRate is > 0m
            && string.Equals(ExpenseCurrencyRules.NormalizeCode(savedCurrency), code, StringComparison.Ordinal))
            return ExpenseCurrencyRules.NormalizeRate(code, savedRate.Value, homeCode);
        return ExpenseCurrencyRules.NormalizeRate(code, catalogRate, homeCode);
    }
}

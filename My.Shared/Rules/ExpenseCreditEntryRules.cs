namespace My.Shared.Rules;

/// <summary>
/// How a credit is entered, and the amount cap for a credit taken from a paid line.
/// </summary>
public static class ExpenseCreditEntryRules
{
    public const string FromLine = "Line";
    public const string OnForm = "Form";
    public const int DescriptionMaxLength = 200;

    public static string NormalizeMode(string? value) =>
        string.Equals(value?.Trim(), OnForm, StringComparison.OrdinalIgnoreCase) ? OnForm : FromLine;

    public static bool IsOnForm(string? value) =>
        string.Equals(NormalizeMode(value), OnForm, StringComparison.Ordinal);

    public static decimal HomeAmount(decimal amount, string? currency, decimal rate, string? home)
    {
        if (amount < 0m)
            amount = 0m;
        var used = ExpenseCurrencyRules.IsHome(currency, home) ? 1m : rate;
        return ExpenseCurrencyRules.ToUsd(amount, used);
    }

    /// <summary>
    /// Same currency: the credit cannot exceed the amount spent on the line.
    /// Another currency: the converted amount cannot exceed the line's stored total.
    /// </summary>
    public static string? RejectAmount(
        decimal amount,
        string? creditCurrency,
        decimal lineEntered,
        string? lineCurrency,
        decimal creditRate,
        decimal lineAmountHome)
    {
        if (amount <= 0m)
            return "Amount must be greater than zero.";

        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        var creditCode = ExpenseCurrencyRules.NormalizeCode(creditCurrency);
        var lineCode = ExpenseCurrencyRules.NormalizeCode(lineCurrency);
        if (string.Equals(creditCode, lineCode, StringComparison.Ordinal))
        {
            var cap = decimal.Round(lineEntered < 0m ? 0m : lineEntered, 2, MidpointRounding.AwayFromZero);
            return rounded > cap ? "Amount cannot be more than the line." : null;
        }

        var home = ExpenseCurrencyRules.ToUsd(rounded, creditRate);
        return home > lineAmountHome ? "Amount cannot be more than the line." : null;
    }
}

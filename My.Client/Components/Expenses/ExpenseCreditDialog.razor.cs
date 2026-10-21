using Microsoft.AspNetCore.Components;
using MudBlazor;
using My.Shared.Dtos.Expenses;
using My.Shared.Rules;

namespace My.Client.Components.Expenses;

public sealed class ExpenseCreditDraft
{
    public DateTime Date { get; init; }
    public string Description { get; init; } = "";
    public decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = "";
}

public partial class ExpenseCreditDialog
{
    private DateTime? date;
    private string description = "";
    private decimal amount;
    private string currencyCode = ExpenseCurrencyRules.UsdCode;
    private bool missingDate;
    private bool missingDescription;
    private string? amountError;

    [Parameter] public DateTime DateValue { get; set; }
    [Parameter] public string Description { get; set; } = "";
    [Parameter] public decimal Amount { get; set; }
    [Parameter] public string CurrencyCode { get; set; } = "";
    [Parameter] public IReadOnlyList<ExpenseCurrencyDto> Currencies { get; set; } = [];
    [Parameter] public bool CapToLine { get; set; }
    [Parameter] public decimal MaxAmount { get; set; }
    [Parameter] public string MaxCurrency { get; set; } = "";
    [Parameter] public decimal LineAmountHome { get; set; }
    [Parameter] public Dictionary<string, decimal> Rates { get; set; } = new(StringComparer.Ordinal);

    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    private IEnumerable<ExpenseCurrencyDto> CurrencyChoices
    {
        get
        {
            var code = ExpenseCurrencyRules.NormalizeCode(currencyCode);
            if (Currencies.Any(currency => string.Equals(currency.Code, code, StringComparison.Ordinal)))
                return Currencies;
            return Currencies.Append(new ExpenseCurrencyDto { Code = code, Name = code });
        }
    }

    protected override void OnInitialized()
    {
        date = ExpenseLineRules.CalendarDate(DateValue == default ? DateTime.Today : DateValue);
        description = Description ?? "";
        amount = Amount;
        currencyCode = ExpenseCurrencyRules.NormalizeCode(
            string.IsNullOrWhiteSpace(CurrencyCode) ? ExpenseCurrencyRules.UsdCode : CurrencyCode);
    }

    private void SetDate(DateTime? value)
    {
        date = value == null ? null : ExpenseLineRules.CalendarDate(value.Value);
        if (date != null)
            missingDate = false;
    }

    private void SetAmount(decimal value)
    {
        amount = value;
        amountError = null;
    }

    private void SetCurrency(string value)
    {
        currencyCode = ExpenseCurrencyRules.NormalizeCode(value);
        amountError = null;
    }

    private void Cancel() => MudDialog.Cancel();

    private void Confirm()
    {
        missingDate = date == null;
        missingDescription = string.IsNullOrWhiteSpace(description);
        amountError = null;
        if (!missingDate && !missingDescription && CapToLine)
        {
            var rate = Rates.TryGetValue(currencyCode, out var found) && found > 0m ? found : 1m;
            amountError = ExpenseCreditEntryRules.RejectAmount(
                amount, currencyCode, MaxAmount, MaxCurrency, rate, LineAmountHome);
        }
        else if (!missingDate && !missingDescription && amount <= 0m)
        {
            amountError = "Amount must be greater than zero.";
        }

        if (missingDate || missingDescription || amountError != null || date == null)
            return;

        MudDialog.Close(DialogResult.Ok(new ExpenseCreditDraft
        {
            Date = date.Value,
            Description = description.Trim(),
            Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
            CurrencyCode = currencyCode
        }));
    }
}

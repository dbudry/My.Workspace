using Microsoft.AspNetCore.Components;
using My.Shared.Dtos.Expenses;
using My.Shared.Rules;

namespace My.Client.Components.Expenses;

public partial class ExpenseAmountField
{
    [Parameter] public decimal Amount { get; set; }
    [Parameter] public EventCallback<decimal> AmountChanged { get; set; }
    [Parameter] public string CurrencyCode { get; set; } = ExpenseCurrencyRules.UsdCode;
    [Parameter] public EventCallback<string> CurrencyCodeChanged { get; set; }
    [Parameter] public IEnumerable<ExpenseCurrencyDto> Currencies { get; set; } = [];
    [Parameter] public bool ShowPicker { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public bool PickerDisabled { get; set; }
    [Parameter] public bool Foreign { get; set; }
    [Parameter] public string? CurrencyStyle { get; set; }

    private string DisplayCode => ExpenseCurrencyRules.NormalizeCode(CurrencyCode);

    private string? StartText
    {
        get
        {
            if (ShowPicker && !ExpenseCurrencyRules.IsUsd(CurrencyCode))
                return null;
            return ExpenseCurrencyRules.IsUsd(CurrencyCode)
                ? "$"
                : DisplayCode;
        }
    }

    private Task Choose(string code)
    {
        var normalized = ExpenseCurrencyRules.NormalizeCode(code);
        if (PickerDisabled || string.Equals(normalized, DisplayCode, StringComparison.Ordinal))
            return Task.CompletedTask;
        return CurrencyCodeChanged.InvokeAsync(normalized);
    }
}

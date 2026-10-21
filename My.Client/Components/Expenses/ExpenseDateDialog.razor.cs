using Microsoft.AspNetCore.Components;
using MudBlazor;
using My.Shared.Rules;

namespace My.Client.Components.Expenses;

public partial class ExpenseDateDialog
{
    private DateTime? date;
    private bool missing;

    [Parameter] public string Label { get; set; } = "Date";
    [Parameter] public DateTime Value { get; set; }

    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    protected override void OnInitialized()
    {
        date = ExpenseLineRules.CalendarDate(Value);
    }

    private void SetDate(DateTime? value)
    {
        date = value == null ? null : ExpenseLineRules.CalendarDate(value.Value);
        if (date != null)
            missing = false;
    }

    private void Cancel() => MudDialog.Cancel();

    private void Confirm()
    {
        if (date == null)
        {
            missing = true;
            return;
        }

        MudDialog.Close(DialogResult.Ok(date.Value));
    }
}

using Microsoft.AspNetCore.Components;
using MudBlazor;
using My.Shared.Rules;

namespace My.Client.Components.Expenses;

public partial class ExpenseSubmitConfirmDialog
{
    [Parameter] public string Question { get; set; } = "";
    [Parameter] public string FiledAs { get; set; } = "";
    [Parameter] public string Effect { get; set; } = "";
    [Parameter] public IReadOnlyList<ExpenseSubmitWarningRules.Issue> Issues { get; set; } = [];

    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    private bool HasIssues => Issues.Count > 0;

    public static async Task<bool> AskAsync(
        IDialogService dialogs,
        DateTime coverStart,
        DateTime coverEnd,
        IReadOnlyList<ExpenseSubmitWarningRules.Issue> issues)
    {
        var copy = ExpenseReportRules.SubmitConfirm(coverStart, coverEnd, DateTime.Today);
        var parameters = new DialogParameters<ExpenseSubmitConfirmDialog>
        {
            { dialog => dialog.Question, copy.Question },
            { dialog => dialog.FiledAs, copy.FiledAs },
            { dialog => dialog.Effect, copy.Effect },
            { dialog => dialog.Issues, issues }
        };
        var options = new DialogOptions
        {
            MaxWidth = MaxWidth.Small,
            FullWidth = true,
            CloseOnEscapeKey = true
        };
        var dialog = await dialogs.ShowAsync<ExpenseSubmitConfirmDialog>("Submit report?", parameters, options);
        var result = await dialog.Result;
        return result is { Canceled: false };
    }

    private void Cancel() => MudDialog.Cancel();

    private void Confirm() => MudDialog.Close(DialogResult.Ok(true));
}

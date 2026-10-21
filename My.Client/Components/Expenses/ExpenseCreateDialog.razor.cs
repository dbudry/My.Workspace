using Microsoft.AspNetCore.Components;
using MudBlazor;
using My.Shared.Rules;

namespace My.Client.Components.Expenses;

public partial class ExpenseCreateDialog
{
    private DateTime? coverStart;
    private DateTime? coverEnd;
    private string? coverError;
    private string? chargeToId;
    private string? chargeToName;

    [Parameter] public DateTime CoverStart { get; set; }
    [Parameter] public DateTime CoverEnd { get; set; }

    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    private bool CanCreate =>
        coverStart != null
        && coverEnd != null
        && string.IsNullOrEmpty(coverError)
        && !string.IsNullOrWhiteSpace(chargeToId);

    protected override void OnInitialized()
    {
        coverStart = ExpenseLineRules.CalendarDate(CoverStart);
        coverEnd = ExpenseLineRules.CalendarDate(CoverEnd);
        ValidateCover();
    }

    private void SetStart(DateTime? value)
    {
        coverStart = value == null ? null : ExpenseLineRules.CalendarDate(value.Value);
        ValidateCover();
    }

    private void SetEnd(DateTime? value)
    {
        coverEnd = value == null ? null : ExpenseLineRules.CalendarDate(value.Value);
        ValidateCover();
    }

    private void ValidateCover()
    {
        coverError = coverStart != null && coverEnd != null
            && !ExpenseReportRules.IsValidCoverPeriod(coverStart.Value, coverEnd.Value)
            ? "Cover end date cannot be before cover start date."
            : null;
    }

    private void SetChargeToId(string? organizationId)
    {
        chargeToId = string.IsNullOrWhiteSpace(organizationId) ? null : organizationId.Trim();
        if (chargeToId == null)
            chargeToName = null;
    }

    private void SetChargeToName(string? organizationName)
    {
        if (chargeToId == null || string.IsNullOrWhiteSpace(organizationName))
        {
            chargeToName = null;
            return;
        }

        chargeToName = organizationName.Trim();
    }

    private void Cancel() => MudDialog.Cancel();

    private void Create()
    {
        if (!CanCreate || coverStart == null || coverEnd == null || chargeToId == null)
            return;
        MudDialog.Close(DialogResult.Ok(new ExpenseCreateRequest(coverStart.Value, coverEnd.Value, chargeToId)));
    }
}

public sealed record ExpenseCreateRequest(DateTime CoverStart, DateTime CoverEnd, string ChargeToOrganizationId);

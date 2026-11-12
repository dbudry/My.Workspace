using System.Net.Http.Json;
using My.Client.Components.TimeSubmissions;
using My.Shared.Constants;
using My.Shared.Dtos.TimeSubmission;
using MudBlazor;

namespace My.Client.Services;

/// <summary>
/// Manager actions on a submitted or unsubmitted (employee × month): unsubmit and submit on behalf.
/// Shared by the Team table and the Grid so both show the same dialogs and call the same endpoints.
/// Each method returns true when something changed, so the caller can reload.
/// </summary>
public class SubmissionActionsService
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly IDialogService _dialogService;
    private readonly ISnackbar _snackbar;
    private readonly TimeSubmissionEvents _events;

    public SubmissionActionsService(
        IHttpClientFactory clientFactory,
        IDialogService dialogService,
        ISnackbar snackbar,
        TimeSubmissionEvents events)
    {
        _clientFactory = clientFactory;
        _dialogService = dialogService;
        _snackbar = snackbar;
        _events = events;
    }

    public async Task<bool> SubmitOnBehalfAsync(string userId, string userName, int year, int month)
    {
        var monthLabel = new DateTime(year, month, 1).ToString("MMMM yyyy");
        var confirm = await _dialogService.ShowMessageBoxAsync(
            "Submit for employee?",
            $"Submit {userName}'s {monthLabel} on their behalf? This locks their tracked tasks for that month the same as if they submitted themselves.",
            yesText: "Submit", cancelText: "Cancel");
        if (confirm != true) return false;

        try
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var resp = await client.PostAsJsonAsync(Constants.API.TimeSubmission.CreateOnBehalf,
                new CreateManagerTimeSubmissionDto { UserId = userId, Year = year, Month = month });
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync();
                _snackbar.Add(string.IsNullOrWhiteSpace(detail)
                    ? $"Couldn't submit: {(int)resp.StatusCode}"
                    : detail.Trim('"'), Severity.Error);
                return false;
            }

            _snackbar.Add($"Submitted {userName}'s {new DateTime(year, month, 1):MMM yyyy}.", Severity.Success);
            _events.NotifyChanged();
            return true;
        }
        catch (Exception ex)
        {
            _snackbar.Add($"Couldn't submit: {ex.Message}", Severity.Error);
            return false;
        }
    }

    /// <summary>
    /// Shows the reminder email the server built for this employee and sends it when the manager confirms.
    /// Returns true when it was sent. Nothing in the data changes, so callers need not reload.
    /// </summary>
    public async Task<bool> SendReminderAsync(string userId, string userName, int year, int month)
    {
        try
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var previewResp = await client.GetAsync(
                $"{Constants.API.TimeSubmission.Reminder}?userId={Uri.EscapeDataString(userId)}&year={year}&month={month}");
            if (!previewResp.IsSuccessStatusCode)
            {
                await ShowErrorAsync(previewResp, "Couldn't prepare the reminder");
                return false;
            }
            var preview = await previewResp.Content.ReadFromJsonAsync<UnsubmittedReminderPreviewDto>();
            if (preview == null) return false;

            var dialog = await _dialogService.ShowAsync<UnsubmittedReminderDialog>(
                "Send reminder",
                new DialogParameters { ["Preview"] = preview },
                new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
            var result = await dialog.Result;
            if (result is null or { Canceled: true }) return false;

            var sendResp = await client.PostAsJsonAsync(Constants.API.TimeSubmission.Reminder,
                new SendUnsubmittedReminderDto { UserId = userId, Year = year, Month = month });
            if (!sendResp.IsSuccessStatusCode)
            {
                await ShowErrorAsync(sendResp, "Couldn't send the reminder");
                return false;
            }

            _snackbar.Add($"Reminder sent to {userName}.", Severity.Success);
            return true;
        }
        catch (Exception ex)
        {
            _snackbar.Add($"Couldn't send the reminder: {ex.Message}", Severity.Error);
            return false;
        }
    }

    private async Task ShowErrorAsync(HttpResponseMessage resp, string fallback)
    {
        var detail = (await resp.Content.ReadAsStringAsync()).Trim('"');
        _snackbar.Add(string.IsNullOrWhiteSpace(detail) ? $"{fallback}: {(int)resp.StatusCode}" : detail, Severity.Error);
    }

    public async Task<bool> UnsubmitAsync(string timeSubmissionId, string userName, int year, int month)
    {
        if (string.IsNullOrEmpty(timeSubmissionId)) return false;

        try
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var monthLabel = new DateTime(year, month, 1).ToString("MMMM yyyy");
            var corrections = await client.GetFromJsonAsync<TimeSubmissionCorrectionsDto>(
                $"{Constants.API.TimeSubmission.GetCorrections}{timeSubmissionId}/corrections");

            string? reconciliation = null;
            if (corrections?.Items.Any(i => i.Kind == "Alias") == true)
            {
                var parameters = new DialogParameters
                {
                    ["EmployeeName"] = userName,
                    ["MonthLabel"] = monthLabel,
                    ["DirectCorrectionCount"] = corrections.DirectCorrectionCount,
                    ["AliasItems"] = corrections.Items.Where(i => i.Kind == "Alias").ToList()
                };
                var dialog = await _dialogService.ShowAsync<UnsubmitReconciliationDialog>(
                    "Unsubmit with corrections",
                    parameters,
                    new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
                var dialogResult = await dialog.Result;
                if (dialogResult is null or { Canceled: true })
                    return false;

                reconciliation = dialogResult.Data as string;
            }
            else if (corrections?.DirectCorrectionCount > 0)
            {
                var warn = await _dialogService.ShowMessageBoxAsync(
                    "Unsubmit with direct corrections?",
                    $"{userName}'s {monthLabel} has {corrections.DirectCorrectionCount} direct correction(s). Unsubmitting lets the employee edit entries, but correction audit history is kept.",
                    yesText: "Unsubmit", cancelText: "Cancel");
                if (warn != true) return false;
            }
            else
            {
                var confirm = await _dialogService.ShowMessageBoxAsync(
                    "Unsubmit?",
                    $"Unsubmit {userName}'s {monthLabel} so they can edit their tracked tasks again?",
                    yesText: "Unsubmit", cancelText: "Cancel");
                if (confirm != true) return false;
            }

            HttpResponseMessage resp;
            if (!string.IsNullOrEmpty(reconciliation))
            {
                resp = await client.PostAsJsonAsync(
                    $"{Constants.API.TimeSubmission.Unsubmit}{timeSubmissionId}/unsubmit",
                    new UnsubmitTimeSubmissionDto { AliasReconciliation = reconciliation });
            }
            else if (corrections?.DirectCorrectionCount > 0)
            {
                resp = await client.PostAsJsonAsync(
                    $"{Constants.API.TimeSubmission.Unsubmit}{timeSubmissionId}/unsubmit",
                    new UnsubmitTimeSubmissionDto());
            }
            else
            {
                resp = await client.DeleteAsync($"{Constants.API.TimeSubmission.Delete}{timeSubmissionId}");
            }

            resp.EnsureSuccessStatusCode();
            _snackbar.Add($"Unsubmitted {userName}'s {new DateTime(year, month, 1):MMM yyyy}.", Severity.Success);
            _events.NotifyChanged();
            return true;
        }
        catch (Exception ex)
        {
            _snackbar.Add($"Couldn't unsubmit: {ex.Message}", Severity.Error);
            return false;
        }
    }
}

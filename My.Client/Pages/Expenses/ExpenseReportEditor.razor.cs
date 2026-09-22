using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using MudBlazor;
using My.Client.Extensions;
using My.Shared.Constants;
using My.Shared.Dtos.Expenses;
using My.Shared.Rules;
using My.Shared.Validation;

namespace My.Client.Pages.Expenses;

public partial class ExpenseReportEditor
{
    private static readonly Regex LineProperty = new(@"^Lines\[(\d+)\]\.(Date|Description)$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions FingerprintJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Parameter] public string Id { get; set; } = "";

    private bool isLoading = true;
    private bool isSaving;
    private bool isSubmitting;
    private bool isUnsubmitting;
    private bool isReimbursing;
    private bool isUndoingReimburse;

    private bool isManager;
    private string? currentUserId;
    private ExpenseReportDto? report;
    private ExpenseContextDto? meta;
    private HttpClient client = null!;
    private string savedFingerprint = "";
    private readonly HashSet<int> lineDateErrors = [];
    private readonly HashSet<int> lineDescriptionErrors = [];
    private string? coverPeriodError;
    private ExpenseLineDto? receiptTarget;
    private ExpenseLineDto? attachingLine;
    private int receiptPickerKey;

    [CascadingParameter]
    private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

    [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IJSRuntime Js { get; set; } = null!;

    private bool isOwner =>
        report != null
        && !string.IsNullOrEmpty(currentUserId)
        && string.Equals(report.UserId, currentUserId, StringComparison.Ordinal);

    private bool canEdit => report != null && isOwner && ExpenseStatusRules.IsDraft(report.Status);

    private bool canUnsubmit =>
        report != null && isManager && ExpenseStatusRules.IsSubmitted(report.Status);

    private bool canReimburse =>
        report != null && isManager && ExpenseStatusRules.IsSubmitted(report.Status);

    private bool canUndoReimburse =>
        report != null && isManager && ExpenseStatusRules.IsReimbursed(report.Status);

    private bool isDirty =>
        report != null && canEdit && Fingerprint(report) != savedFingerprint;

    private string pageTitle => report == null
        ? "Expense report"
        : string.IsNullOrWhiteSpace(report.EmployeeNameSnapshot) || isOwner
            ? $"{MonthLabel(report.Year, report.Month)} expenses"
            : $"{MonthLabel(report.Year, report.Month)} — {report.EmployeeNameSnapshot}";

    private string pageDescription => report == null
        ? ""
        : ExpenseStatusRules.IsReimbursed(report.Status)
            ? "Reimbursed — this report is locked."
            : ExpenseStatusRules.IsSubmitted(report.Status)
                ? "Submitted — this report is locked."
                : canEdit
                    ? isDirty
                        ? "Draft with unsaved changes."
                        : "Draft."
                    : "Draft. Only the owner can edit.";

    private IReadOnlyList<ExpenseChoiceDto> contextChoices =>
        meta?.Categories ?? [];

    private IReadOnlyList<ExpenseChoiceDto> transportCodes =>
        meta?.TransportationCodes ?? [];

    private IReadOnlyList<ExpenseChoiceDto> miscCodes =>
        meta?.MiscellaneousCodes ?? [];

    private IReadOnlyList<(string Label, decimal Amount)> CategoryTotals
    {
        get
        {
            if (report == null) return [];
            return report.Lines
                .GroupBy(l => l.Category, StringComparer.Ordinal)
                .Select(g => (
                    Label: contextChoices.FirstOrDefault(c => c.Value == g.Key)?.Label ?? g.Key,
                    Amount: g.Sum(l => l.Amount)))
                .Where(t => t.Amount != 0)
                .OrderBy(t => t.Label)
                .ToList();
        }
    }

    private decimal GrandTotal => report?.Lines.Sum(l => l.Amount) ?? 0m;

    private bool hasPersonalCarMiles =>
        report?.Lines.Any(l => ExpenseCategoryRules.IsPersonalCar(l.Category, l.TransportationCode)
            && l.Miles.GetValueOrDefault() > 0) == true;

    protected override async Task OnInitializedAsync()
    {
        client = ClientFactory.CreateClient(Constants.API.ClientName);
        var authState = await AuthenticationStateTask;
        currentUserId = authState.User.FindFirst(Constants.Claims.AppUserId)?.Value;
        isManager = Constants.Roles.HasScopedAccess(
            authState.User, Constants.Scopes.Expenses, Constants.Roles.Manager);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        try
        {
            var contextTask = client.GetFromJsonAsync<ExpenseContextDto>(Constants.API.Expenses.Context);
            var reportTask = client.GetAsync(Constants.API.Expenses.ReportById + Id);
            await Task.WhenAll(contextTask, reportTask);

            meta = contextTask.Result;
            var response = reportTask.Result;
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                report = null;
                return;
            }
            response.EnsureSuccessStatusCode();
            report = await response.Content.ReadFromJsonAsync<ExpenseReportDto>();
            NormalizeLineDates();
            RememberSaved();
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't load the expense report.");
            report = null;
        }
        finally
        {
            isLoading = false;
        }
    }

    private static string PdfFileName(ExpenseReportDto report)
    {
        var who = string.Join("_", (report.EmployeeNameSnapshot ?? "Employee")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return $"{report.Year}_{report.Month:00}_{who}_Expenses.pdf";
    }

    private async Task SubmitAsync()
    {
        if (report == null) return;
        if (isDirty)
        {
            await SaveAsync();
            if (isDirty) return;
        }

        var blocked = ExpenseReportRules.SubmitBlockedReason(report.Status, report.Lines.Count);
        if (blocked != null)
        {
            Snackbar.Add(blocked, Severity.Warning);
            return;
        }

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Submit report?",
            $"Submit {MonthLabel(report.Year, report.Month)}? This locks the report and files the statement PDF on Drive.",
            yesText: "Submit", cancelText: "Cancel");
        if (confirmed != true) return;

        isSubmitting = true;
        try
        {
            var response = await client.PostAsync(Constants.API.Expenses.Submit(report.ExpenseReportId), null);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(ApiErrorBodyRules.Format(
                    await response.Content.ReadAsStringAsync(),
                    "Couldn't submit the report."), Severity.Error);
                return;
            }

            report = await response.Content.ReadFromJsonAsync<ExpenseReportDto>();
            NormalizeLineDates();
            RememberSaved();
            Snackbar.Add("Submitted.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't submit the report.");
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private async Task UnsubmitAsync()
    {
        if (report == null) return;
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Unsubmit report?",
            "Unlock this report so it can be edited?",
            yesText: "Unsubmit", cancelText: "Cancel");
        if (confirmed != true) return;

        isUnsubmitting = true;
        try
        {
            var response = await client.PostAsync(Constants.API.Expenses.Unsubmit(report.ExpenseReportId), null);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(ApiErrorBodyRules.Format(
                    await response.Content.ReadAsStringAsync(),
                    "Couldn't unsubmit the report."), Severity.Error);
                return;
            }

            report = await response.Content.ReadFromJsonAsync<ExpenseReportDto>();
            NormalizeLineDates();
            RememberSaved();
            Snackbar.Add("Unsubmitted.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't unsubmit the report.");
        }
        finally
        {
            isUnsubmitting = false;
        }
    }

    private async Task ReimburseAsync()
    {
        if (report == null) return;
        var blocked = ExpenseReportRules.ReimburseBlockedReason(report.Status);
        if (blocked != null)
        {
            Snackbar.Add(blocked, Severity.Warning);
            return;
        }

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Mark reimbursed?",
            "Record that this report has been paid?",
            yesText: "Reimburse", cancelText: "Cancel");
        if (confirmed != true) return;

        isReimbursing = true;
        try
        {
            var response = await client.PostAsync(Constants.API.Expenses.Reimburse(report.ExpenseReportId), null);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(ApiErrorBodyRules.Format(
                    await response.Content.ReadAsStringAsync(),
                    "Couldn't mark the report reimbursed."), Severity.Error);
                return;
            }

            report = await response.Content.ReadFromJsonAsync<ExpenseReportDto>();
            NormalizeLineDates();
            RememberSaved();
            Snackbar.Add("Reimbursed.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't mark the report reimbursed.");
        }
        finally
        {
            isReimbursing = false;
        }
    }

    private async Task UndoReimburseAsync()
    {
        if (report == null) return;
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Undo reimbursed?",
            "Return this report to submitted?",
            yesText: "Undo", cancelText: "Cancel");
        if (confirmed != true) return;

        isUndoingReimburse = true;
        try
        {
            var response = await client.PostAsync(Constants.API.Expenses.UndoReimburse(report.ExpenseReportId), null);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(ApiErrorBodyRules.Format(
                    await response.Content.ReadAsStringAsync(),
                    "Couldn't undo reimbursed."), Severity.Error);
                return;
            }

            report = await response.Content.ReadFromJsonAsync<ExpenseReportDto>();
            NormalizeLineDates();
            RememberSaved();
            Snackbar.Add("Returned to submitted.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't undo reimbursed.");
        }
        finally
        {
            isUndoingReimburse = false;
        }
    }

    private async Task SaveAsync(bool showToast = true)
    {
        if (report == null) return;
        if (!ValidateLocal())
        {
            Snackbar.Add("Fix the highlighted fields, then save.", Severity.Warning);
            return;
        }

        isSaving = true;
        try
        {
            var dto = ToUpdateDto(report);
            var response = await client.PutAsJsonAsync(Constants.API.Expenses.ReportById + report.ExpenseReportId, dto);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                Snackbar.Add(ApiErrorBodyRules.Format(body, "Couldn't save the report."), Severity.Error);
                return;
            }

            report = await response.Content.ReadFromJsonAsync<ExpenseReportDto>();
            NormalizeLineDates();
            RememberSaved();
            if (showToast)
                Snackbar.Add("Saved.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't save the report.");
        }
        finally
        {
            isSaving = false;
        }
    }

    private bool ValidateLocal()
    {
        lineDateErrors.Clear();
        lineDescriptionErrors.Clear();
        ValidateCoverPeriod();
        if (report == null) return false;

        var result = new UpdateExpenseReportDtoValidator().Validate(ToUpdateDto(report));
        foreach (var error in result.Errors)
        {
            var match = LineProperty.Match(error.PropertyName);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var index))
            {
                if (match.Groups[2].Value == "Date")
                    lineDateErrors.Add(index);
                else
                    lineDescriptionErrors.Add(index);
            }
        }

        return result.IsValid;
    }

    private void AddLine()
    {
        if (report == null) return;
        var today = ExpenseLineRules.CalendarDate(DateTime.Today);
        var date = today.Year == report.Year && today.Month == report.Month
            ? today
            : new DateTime(report.Year, report.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        report.Lines.Add(new ExpenseLineDto
        {
            ExpenseLineId = Guid.NewGuid().ToString("N"),
            Date = date,
            Description = "",
            Category = ExpenseCategoryRules.Software,
            SortOrder = report.Lines.Count == 0 ? 0 : report.Lines.Max(l => l.SortOrder) + 1
        });
    }

    private async Task RemoveLineAsync(ExpenseLineDto line)
    {
        var label = string.IsNullOrWhiteSpace(line.Description) ? "this line" : $"“{line.Description}”";
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Remove line?",
            $"Remove {label} from this report?",
            yesText: "Remove", cancelText: "Cancel");
        if (confirmed != true) return;

        var index = report?.Lines.IndexOf(line) ?? -1;
        report?.Lines.Remove(line);
        if (index >= 0)
        {
            ExpenseLineRules.ShiftIndicesAfterRemoval(lineDateErrors, index);
            ExpenseLineRules.ShiftIndicesAfterRemoval(lineDescriptionErrors, index);
        }
    }

    private async Task OnBeforeInternalNav(LocationChangingContext ctx)
    {
        if (!isDirty) return;
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Unsaved changes",
            "Leave without saving? Your line items will be lost.",
            yesText: "Leave", cancelText: "Stay");
        if (confirmed != true)
            ctx.PreventNavigation();
    }

    private void SetCategory(ExpenseLineDto line, string value)
    {
        line.Category = value;
        if (!ExpenseCategoryRules.IsPersonalCar(value, line.TransportationCode)
            && value != ExpenseCategoryRules.Transportation)
            line.Miles = null;
        if (value != ExpenseCategoryRules.Transportation)
            line.TransportationCode = null;
        if (value != ExpenseCategoryRules.Miscellaneous)
            line.MiscellaneousCode = null;
        if (value != ExpenseCategoryRules.Meals)
        {
            line.MealBreakfast = false;
            line.MealLunch = false;
            line.MealDinner = false;
        }
        if (!ExpenseCategoryRules.IsPersonalCar(value, line.TransportationCode))
            line.Amount = 0;
        RecomputeMileage(line);
    }

    private void SetTransportationCode(ExpenseLineDto line, string? value)
    {
        line.TransportationCode = string.IsNullOrWhiteSpace(value) ? null : value;
        if (!ExpenseCategoryRules.IsPersonalCar(line.Category, line.TransportationCode))
        {
            line.Miles = null;
            line.Amount = 0;
        }
        RecomputeMileage(line);
    }

    private void SetMiscellaneousCode(ExpenseLineDto line, string? value)
    {
        line.MiscellaneousCode = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private void SetAmount(ExpenseLineDto line, decimal value)
    {
        if (ExpenseCategoryRules.IsPersonalCar(line.Category, line.TransportationCode))
            return;
        line.Amount = value;
    }

    private void SetMiles(ExpenseLineDto line, decimal? miles)
    {
        line.Miles = miles;
        RecomputeMileage(line);
    }

    private void RecomputeMileage(ExpenseLineDto line)
    {
        if (report == null) return;
        if (!ExpenseCategoryRules.IsPersonalCar(line.Category, line.TransportationCode)) return;
        line.Amount = ExpenseLineRules.ComputeAmount(
            line.Category, line.Miles, line.Amount, report.MileageRateSnapshot, line.TransportationCode);
    }

    private void SetLineDate(ExpenseLineDto line, DateTime? value)
    {
        if (value is null) return;
        var next = ExpenseLineRules.CalendarDate(value.Value);
        if (report != null && !ExpenseReportRules.IsLineDateInMonth(next, report.Year, report.Month))
            return;
        if (line.Date.Date == next.Date) return;
        line.Date = next;
        var index = report?.Lines.IndexOf(line) ?? -1;
        if (index >= 0) lineDateErrors.Remove(index);
    }

    private void SetCoverStart(DateTime? value)
    {
        if (value is null || report is null) return;
        var next = ExpenseLineRules.CalendarDate(value.Value);
        if (!ExpenseReportRules.IsLineDateInMonth(next, report.Year, report.Month))
            return;
        report.CoverStart = next;
        ValidateCoverPeriod();
    }

    private void SetCoverEnd(DateTime? value)
    {
        if (value is null || report is null) return;
        var next = ExpenseLineRules.CalendarDate(value.Value);
        if (!ExpenseReportRules.IsLineDateInMonth(next, report.Year, report.Month))
            return;
        report.CoverEnd = next;
        ValidateCoverPeriod();
    }

    private void ValidateCoverPeriod()
    {
        if (report is null) return;
        coverPeriodError = ExpenseReportRules.IsValidCoverPeriod(report.CoverStart, report.CoverEnd)
            ? null
            : "Cover end date cannot be before cover start date.";
    }

    private DateTime? ReportMonthStart =>
        report is null ? null : ExpenseReportRules.DefaultCoverPeriod(report.Year, report.Month).CoverStart;

    private DateTime? ReportMonthEnd =>
        report is null ? null : ExpenseReportRules.DefaultCoverPeriod(report.Year, report.Month).CoverEnd;

    private void SetLineDescription(ExpenseLineDto line, string value)
    {
        line.Description = value;
        var index = report?.Lines.IndexOf(line) ?? -1;
        if (index >= 0) lineDescriptionErrors.Remove(index);
    }

    private async Task BeginAttach(ExpenseLineDto line)
    {
        receiptTarget = line;
        try
        {
            await Js.InvokeVoidAsync("eval", "document.getElementById('expense-receipt-input')?.click()");
        }
        catch
        {
            Snackbar.Add("Couldn't open the file picker.", Severity.Error);
        }
    }

    private async Task OnReceiptChosenAsync(InputFileChangeEventArgs e)
    {
        var files = e.GetMultipleFiles(ExpenseReceiptRules.MaxFilesPerLine);
        var line = receiptTarget;
        receiptTarget = null;
        try
        {
            if (files.Count == 0 || line == null || report == null) return;
            attachingLine = line;

            if (!ExpenseReceiptRules.TryValidateCount(line.Receipts.Count, files.Count, out var countError))
            {
                Snackbar.Add(countError ?? "Couldn't attach those files.", Severity.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(line.ExpenseLineId) || isDirty)
            {
                await SaveAsync(showToast: false);
                if (report == null) return;
                line = MatchLineAfterSave(line);
                attachingLine = line;
                if (line == null || string.IsNullOrWhiteSpace(line.ExpenseLineId))
                {
                    Snackbar.Add("Save the line, then attach the receipt.", Severity.Warning);
                    return;
                }
            }

            if (ExpenseReportRules.IsLineDateAfterCoverEnd(line.Date, report.CoverEnd))
            {
                Snackbar.Add(
                    $"Heads up: this line's date ({line.Date:MM/dd/yyyy}) is after the report's cover end ({report.CoverEnd:MM/dd/yyyy}).",
                    Severity.Warning);
            }

            var attached = 0;
            var attachedHeic = false;
            string? lastError = null;
            foreach (var file in files)
            {
                if (!ExpenseReceiptRules.TryValidate(file.Name, (int)Math.Min(file.Size, int.MaxValue), out var error))
                {
                    lastError = error;
                    continue;
                }
                if (ExpensePacketReceiptRules.IsNestedExpensePacket(file.Name))
                {
                    lastError = ExpensePacketReceiptRules.RejectMessage;
                    continue;
                }

                byte[] bytes;
                try
                {
                    await using var stream = file.OpenReadStream(ExpenseReceiptRules.MaxBytes);
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    bytes = ms.ToArray();
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    continue;
                }

                if (ExpensePacketReceiptRules.IsNestedExpensePacket(file.Name, bytes))
                {
                    lastError = ExpensePacketReceiptRules.RejectMessage;
                    continue;
                }

                try
                {
                    var payload = new UploadExpenseReceiptDto
                    {
                        FileName = file.Name,
                        MimeType = file.ContentType,
                        ContentBase64 = Convert.ToBase64String(bytes)
                    };
                    var response = await client.PostAsJsonAsync(
                        Constants.API.Expenses.LineReceipts(report.ExpenseReportId, line.ExpenseLineId!), payload);
                    if (!response.IsSuccessStatusCode)
                    {
                        lastError = ApiErrorBodyRules.Format(
                            await response.Content.ReadAsStringAsync(),
                            "Couldn't attach the receipt.");
                        continue;
                    }

                    var saved = await response.Content.ReadFromJsonAsync<ExpenseReceiptDto>();
                    if (saved != null)
                    {
                        line.Receipts.Add(saved);
                        attached++;
                        if (ExpenseReceiptRules.IsHeic(file.Name, file.ContentType))
                            attachedHeic = true;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }

            if (attached == files.Count)
            {
                Snackbar.Add(attached == 1 ? "Receipt attached." : $"{attached} receipts attached.", Severity.Success);
                if (attachedHeic)
                    Snackbar.Add("HEIC photos are converted to JPEG for the statement.", Severity.Info);
            }
            else if (attached > 0)
                Snackbar.Add($"{attached} attached. {lastError ?? "The rest could not be attached."}", Severity.Warning);
            else
                Snackbar.Add(lastError ?? "Couldn't attach the receipt.", Severity.Error);
        }
        finally
        {
            attachingLine = null;
            receiptPickerKey++;
        }
    }

    private ExpenseLineDto? MatchLineAfterSave(ExpenseLineDto previous)
    {
        if (report == null) return null;
        if (!string.IsNullOrWhiteSpace(previous.ExpenseLineId))
            return report.Lines.FirstOrDefault(l => l.ExpenseLineId == previous.ExpenseLineId);
        return report.Lines.FirstOrDefault(l =>
            l.Date.Date == previous.Date.Date
            && string.Equals(l.Description, previous.Description, StringComparison.Ordinal)
            && string.Equals(l.Category, previous.Category, StringComparison.Ordinal)
            && l.SortOrder == previous.SortOrder);
    }

    private async Task DownloadReceiptAsync(ExpenseReceiptDto receipt)
    {
        try
        {
            var bytes = await client.GetByteArrayAsync(Constants.API.Expenses.ReceiptMedia(receipt.ExpenseReceiptId));
            var b64 = Convert.ToBase64String(bytes);
            var mime = string.IsNullOrWhiteSpace(receipt.MimeType) ? "application/octet-stream" : receipt.MimeType;
            await Js.InvokeVoidAsync("fileSave.download", b64, mime, receipt.OriginalFileName);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't download the receipt.");
        }
    }

    private async Task RemoveReceiptAsync(ExpenseLineDto line, ExpenseReceiptDto receipt)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Remove receipt?",
            $"Remove {receipt.OriginalFileName} from this line?",
            yesText: "Remove", cancelText: "Cancel");
        if (confirmed != true) return;

        try
        {
            var response = await client.DeleteAsync(Constants.API.Expenses.ReceiptById(receipt.ExpenseReceiptId));
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                Snackbar.Add(ApiErrorBodyRules.Format(body, "Couldn't remove the receipt."), Severity.Error);
                return;
            }

            line.Receipts.RemoveAll(r => r.ExpenseReceiptId == receipt.ExpenseReceiptId);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't remove the receipt.");
        }
    }

    private void NormalizeLineDates()
    {
        if (report == null) return;
        foreach (var line in report.Lines)
            line.Date = ExpenseLineRules.CalendarDate(line.Date);
    }

    private void RememberSaved()
    {
        savedFingerprint = report == null ? "" : Fingerprint(report);
        lineDateErrors.Clear();
        lineDescriptionErrors.Clear();
    }

    private static UpdateExpenseReportDto ToUpdateDto(ExpenseReportDto report) => new()
    {
        CoverStart = report.CoverStart,
        CoverEnd = report.CoverEnd,
        Purpose = report.Purpose,
        Lines = report.Lines
    };

    private static string Fingerprint(ExpenseReportDto report) =>
        JsonSerializer.Serialize(new
        {
            CoverStart = report.CoverStart.ToString("yyyy-MM-dd"),
            CoverEnd = report.CoverEnd.ToString("yyyy-MM-dd"),
            Purpose = report.Purpose ?? "",
            Lines = report.Lines.Select(l => new
            {
                Date = l.Date.ToString("yyyy-MM-dd"),
                Description = l.Description ?? "",
                Category = l.Category ?? "",
                Amount = decimal.Round(l.Amount, 2, MidpointRounding.AwayFromZero),
                Miles = l.Miles,
                TransportationCode = l.TransportationCode ?? "",
                MiscellaneousCode = l.MiscellaneousCode ?? "",
                l.MealBreakfast,
                l.MealLunch,
                l.MealDinner,
                l.SortOrder
            })
        }, FingerprintJson);

    private static string MonthLabel(int year, int month) =>
        new DateTime(year, month, 1).ToString("MMMM yyyy");
}

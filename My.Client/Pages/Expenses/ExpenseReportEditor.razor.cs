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
using My.Client.Components.Expenses;
using My.Client.Extensions;
using My.Client.Services;
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
    private readonly List<PendingReceipt> pendingReceipts = [];
    private readonly HashSet<string> removedReceiptIds = new(StringComparer.Ordinal);
    private readonly HashSet<int> lineDateErrors = [];
    private readonly HashSet<int> lineDescriptionErrors = [];
    private readonly HashSet<int> lineCategoryErrors = [];
    private readonly HashSet<int> lineMealTypeErrors = [];
    private readonly HashSet<int> lineHeadCountErrors = [];
    private readonly List<string> validationMessages = [];
    private string? coverPeriodError;
    private ExpenseLineDto? receiptTarget;
    private ExpenseLineDto? attachingLine;
    private int receiptPickerKey;
    private string editorView = ExpenseCurrencyRules.EditorDetail;
    private string currencyLayout = ExpenseCurrencyRules.LayoutMixed;
    private string defaultCurrency = ExpenseCurrencyRules.UsdCode;
    private string? openLineId;
    private string? activeCurrency;
    private string? addCurrencyPick;
    private bool expensePrefsReady;
    private readonly HashSet<string> extraTabs = new(StringComparer.Ordinal);

    [CascadingParameter]
    private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

    [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IJSRuntime Js { get; set; } = null!;
    [Inject] private UserSettingsService SettingsService { get; set; } = null!;

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



    private bool canMarkReturn =>
        report != null
        && report.CreditBucketEnabled
        && !ExpenseCreditEntryRules.IsOnForm(report.CreditEntryMode)
        && ExpenseStatusRules.IsReimbursed(report.Status)
        && (isOwner || isManager);

    private bool UserEntersRates => report?.UserEntersRates == true;

    private bool ShowFormCreditEntry =>
        report != null
        && report.CreditBucketEnabled
        && ExpenseCreditEntryRules.IsOnForm(report.CreditEntryMode);

    private bool ShowFormCredits =>
        ShowFormCreditEntry || (report?.CreditBucketEnabled == true && report.FormCredits.Count > 0);

    private string CompanyCode =>
        ExpenseCurrencyRules.NormalizeCode(
            !string.IsNullOrWhiteSpace(report?.CompanyCurrencyCode)
                ? report.CompanyCurrencyCode
                : meta?.CompanyCurrencyCode);

    private string HomeCurrencyLabel
    {
        get
        {
            if (ExpenseCurrencyRules.IsUsd(CompanyCode))
                return "US dollars";
            var name = meta?.Currencies.FirstOrDefault(currency =>
                string.Equals(currency.Code, CompanyCode, StringComparison.Ordinal))?.Name;
            return string.IsNullOrWhiteSpace(name) ? CompanyCode : name;
        }
    }

    private string CoverTotalLabel => $"Total in {HomeCurrencyLabel}";

    private sealed record CoverRateRow(string Code, decimal Rate);

    private IReadOnlyList<CoverRateRow> CoverRates
    {
        get
        {
            if (report == null || !report.UserEntersRates)
                return [];
            var codes = new List<string>();
            void Add(string? code)
            {
                var normalized = ExpenseCurrencyRules.NormalizeCode(code);
                if (ExpenseCurrencyRules.IsHome(normalized, CompanyCode))
                    return;
                if (!codes.Contains(normalized, StringComparer.Ordinal))
                    codes.Add(normalized);
            }

            foreach (var line in report.Lines)
            {
                if (ExpenseCategoryRules.IsPersonalCar(line.Category, line.TransportationCode))
                    continue;
                Add(line.CurrencyCode);
            }

            foreach (var credit in report.FormCredits)
                Add(credit.CurrencyCode);
            return codes.Select(code => new CoverRateRow(code, ExistingRate(code) ?? 0m)).ToList();
        }
    }

    private bool formView => editorView == ExpenseCurrencyRules.EditorForm;

    private bool grouped => currencyLayout == ExpenseCurrencyRules.LayoutGrouped;

    private bool isDirty =>
        report != null && canEdit && (Fingerprint(report) != savedFingerprint || HasUnsavedReceipts);

    private bool HasUnsavedReceipts => pendingReceipts.Count > 0 || removedReceiptIds.Count > 0;

    private string pageTitle
    {
        get
        {
            if (report == null) return "Expense report";
            var label = ExpenseStatusRules.IsDraft(report.Status)
                ? ExpenseReportRules.CoverRangeLabel(report.CoverStart, report.CoverEnd)
                : ExpenseReportRules.FilingMonthLabel(report.Status, report.Year, report.Month);
            if (string.IsNullOrWhiteSpace(report.EmployeeNameSnapshot) || isOwner)
                return $"{label} expenses";
            return $"{label} — {report.EmployeeNameSnapshot}";
        }
    }

    private string statusNote => report == null
        ? ""
        : ExpenseStatusRules.IsLocked(report.Status)
            ? "Locked."
            : !canEdit
                ? "Only the owner can edit."
                : isDirty
                    ? "Unsaved changes."
                    : "";

    private IReadOnlyList<ExpenseChoiceDto> contextChoices =>
        meta?.Categories ?? [];

    private IReadOnlyList<ExpenseChoiceDto> transportCodes =>
        meta?.TransportationCodes ?? [];

    private IReadOnlyList<ExpenseChoiceDto> miscCodes =>
        meta?.MiscellaneousCodes ?? [];

    private ExpenseReportMoney LiveMoney
    {
        get
        {
            if (report == null)
            {
                return new ExpenseReportMoney();
            }

            var moneyLines = report.Lines.Select(line => new ExpenseMoneyLine
            {
                LineId = line.ExpenseLineId ?? "",
                CurrencyCode = line.CurrencyCode,
                Color = CurrencyColor(line.CurrencyCode),
                SpentAmount = line.SpentAmount,
                AmountUsd = line.Amount,
                IsReturned = line.IsReturned
            }).ToList();

            var lineCredits = report.CreditBucketEnabled
                && !ExpenseCreditEntryRules.IsOnForm(report.CreditEntryMode);
            IReadOnlyDictionary<string, decimal>? applied = null;
            if (lineCredits)
            {
                var (sources, pool) = CreditPool();
                applied = ExpenseCreditRules.AppliedByLine(sources, pool, CompanyCode);
            }

            var formUsd = report.CreditBucketEnabled
                ? report.FormCredits.Sum(credit => ExpenseCreditEntryRules.HomeAmount(
                    credit.Amount, credit.CurrencyCode, credit.ExchangeRateToUsd, CompanyCode))
                : 0m;
            return ExpenseReportMoneyRules.Summarize(
                moneyLines, applied, report.CreditBucketEnabled, formUsd, CompanyCode);
        }
    }

    private bool hasPersonalCarMiles =>
        report?.Lines.Any(l => ExpenseCategoryRules.IsPersonalCar(l.Category, l.TransportationCode)
            && l.Miles.GetValueOrDefault() > 0) == true;

    private IEnumerable<ExpenseChoiceDto> CodeOptions(IReadOnlyList<ExpenseChoiceDto> options, string? current)
    {
        if (!string.IsNullOrWhiteSpace(current)
            && !options.Any(option => string.Equals(option.Value, current, StringComparison.OrdinalIgnoreCase)))
        {
            return options.Append(new ExpenseChoiceDto { Value = current, Label = current });
        }

        return options;
    }

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
            ApplyContextPreferences();
            NormalizeLineDates();
            NormalizeLoadedMoney();
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

    private static string PdfFileName(ExpenseReportDto report) =>
        ExpenseDriveNamingRules.StatementFileName(report.CoverStart, report.CoverEnd, report.EmployeeNameSnapshot);

    private async Task<bool> RefuseSubmitAsync()
    {
        if (report == null) return true;
        var blocked = ExpenseReportRules.SubmitBlockedReason(
            report.Status,
            report.Lines.Count,
            report.ChargeToOrganizationId);
        if (blocked == null)
            return false;

        await DialogService.ShowMessageBoxAsync("Can't submit", blocked, yesText: "OK");
        return true;
    }

    private async Task SubmitAsync()
    {
        if (report == null || isSubmitting) return;

        if (await RefuseSubmitAsync())
            return;

        isSubmitting = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            if (isDirty)
            {
                await SaveAsync();
                if (isDirty) return;
            }

            if (await RefuseSubmitAsync())
                return;

            var issues = ExpenseSubmitWarningRules.Issues(report.Lines.Select(line =>
                new ExpenseSubmitWarningRules.Line(
                    line.Date, line.Description, line.Amount, line.Category,
                    line.MealType, line.HeadCount, ShownReceipts(line).Count)));
            MarkIncompleteMeals();
            await InvokeAsync(StateHasChanged);
            var confirmed = await ExpenseSubmitConfirmDialog.AskAsync(
                DialogService, report.CoverStart, report.CoverEnd, issues);
            if (!confirmed) return;

            var response = await client.PostAsync(Constants.API.Expenses.Submit(report.ExpenseReportId), null);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(ApiErrorBodyRules.Format(
                    await response.Content.ReadAsStringAsync(),
                    "Couldn't submit the report."), Severity.Error);
                return;
            }

            var submitted = await response.Content.ReadFromJsonAsync<SubmitExpenseReportResultDto>();
            report = submitted?.Report;
            NormalizeLineDates();
            NormalizeLoadedMoney();
            RememberSaved();
            var filedLabel = report == null
                ? "Submitted."
                : $"Filed as {ExpenseReportRules.FilingMonthLabel(report.Status, report.Year, report.Month)}.";
            if (!string.IsNullOrWhiteSpace(submitted?.EmailWarning))
                Snackbar.Add(filedLabel + " " + submitted.EmailWarning, Severity.Warning);
            else
                Snackbar.Add(filedLabel, Severity.Success);
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
            NormalizeLoadedMoney();
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

        var date = await AskDateAsync(ExpensePayLabelRules.DateLabel, ExpensePayLabelRules.AskMark);
        if (date == null) return;

        isReimbursing = true;
        try
        {
            var response = await client.PostAsJsonAsync(
                Constants.API.Expenses.Reimburse(report.ExpenseReportId),
                new ReimburseExpenseReportDto { PaymentDate = date.Value });
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(ApiErrorBodyRules.Format(
                    await response.Content.ReadAsStringAsync(),
                    "Couldn't mark the report paid."), Severity.Error);
                return;
            }

            report = await response.Content.ReadFromJsonAsync<ExpenseReportDto>();
            NormalizeLineDates();
            NormalizeLoadedMoney();
            RememberSaved();
            Snackbar.Add(ExpensePayLabelRules.Done, Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't mark the report paid.");
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
            ExpensePayLabelRules.AskUndo,
            "Return this report to submitted? Returned lines on it are cleared.",
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
                    "Couldn't undo payment."), Severity.Error);
                return;
            }

            report = await response.Content.ReadFromJsonAsync<ExpenseReportDto>();
            NormalizeLineDates();
            NormalizeLoadedMoney();
            RememberSaved();
            Snackbar.Add("Returned to submitted.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't undo payment.");
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
            var message = validationMessages.Count == 0
                ? "This report can't be saved yet."
                : string.Join(" ", validationMessages.Distinct());
            Snackbar.Add(message, Severity.Warning);
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
            NormalizeLoadedMoney();
            var receiptError = await CommitReceiptDraftAsync();
            RememberSaved();
            if (receiptError != null)
            {
                Snackbar.Add(receiptError, Severity.Error);
                return;
            }

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
        lineCategoryErrors.Clear();
        validationMessages.Clear();
        ValidateCoverPeriod();
        if (report == null) return false;

        var result = new UpdateExpenseReportDtoValidator().Validate(ToUpdateDto(report));
        foreach (var error in result.Errors)
        {
            if (!string.IsNullOrWhiteSpace(error.ErrorMessage))
                validationMessages.Add(error.ErrorMessage);
            var match = LineProperty.Match(error.PropertyName);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var index))
            {
                if (match.Groups[2].Value == "Date")
                    lineDateErrors.Add(index);
                else if (match.Groups[2].Value == "Category")
                    lineCategoryErrors.Add(index);
                else
                    lineDescriptionErrors.Add(index);
            }
        }

        if (report.UserEntersRates)
        {
            foreach (var rate in CoverRates.Where(rate => rate.Rate <= 0m))
                validationMessages.Add($"Enter the exchange rate for {rate.Code}.");
        }

        return result.IsValid && validationMessages.Count == 0;
    }

    private void AddLine()
    {
        if (report == null) return;
        var today = ExpenseLineRules.CalendarDate(DateTime.Today);
        var coverStart = ExpenseLineRules.CalendarDate(report.CoverStart);
        var coverEnd = ExpenseLineRules.CalendarDate(report.CoverEnd);
        var date = today >= coverStart && today <= coverEnd ? today : coverStart;
        var currency = grouped ? ActiveTabCode() : defaultCurrency;
        var line = new ExpenseLineDto
        {
            ExpenseLineId = Guid.NewGuid().ToString("N"),
            Date = date,
            Description = "",
            CurrencyCode = currency,
            ExchangeRateToUsd = RateForNewLine(currency),
            SortOrder = report.Lines.Count == 0 ? 0 : report.Lines.Max(l => l.SortOrder) + 1
        };
        report.Lines.Add(line);
        if (formView)
            openLineId = line.ExpenseLineId;
    }

    private async Task RemoveLineAsync(ExpenseLineDto line)
    {
        if (report == null) return;

        var label = string.IsNullOrWhiteSpace(line.Description) ? "this line" : $"“{line.Description}”";
        var body = line.Receipts.Count switch
        {
            0 => $"Remove {label} from this report?",
            1 => $"Remove {label} from this report? Its receipt is deleted when you save.",
            _ => $"Remove {label} from this report? Its {line.Receipts.Count} receipts are deleted when you save."
        };
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Remove line?",
            body,
            yesText: "Remove", cancelText: "Cancel");
        if (confirmed != true) return;

        var index = report.Lines.IndexOf(line);
        if (!string.IsNullOrWhiteSpace(line.ExpenseLineId))
            pendingReceipts.RemoveAll(pending => pending.LineId == line.ExpenseLineId);
        report.Lines.Remove(line);
        if (string.Equals(openLineId, line.ExpenseLineId, StringComparison.Ordinal))
            openLineId = null;
        if (index >= 0)
        {
            ExpenseLineRules.ShiftIndicesAfterRemoval(lineDateErrors, index);
            ExpenseLineRules.ShiftIndicesAfterRemoval(lineDescriptionErrors, index);
            ExpenseLineRules.ShiftIndicesAfterRemoval(lineCategoryErrors, index);
            ExpenseLineRules.ShiftIndicesAfterRemoval(lineMealTypeErrors, index);
            ExpenseLineRules.ShiftIndicesAfterRemoval(lineHeadCountErrors, index);
        }
    }

    private async Task OnBeforeInternalNav(LocationChangingContext ctx)
    {
        if (!isDirty) return;
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Unsaved changes",
            "Leave without saving? Changes on this report will be discarded.",
            yesText: "Leave", cancelText: "Stay");
        if (confirmed != true)
            ctx.PreventNavigation();
    }

    private void SetCategory(ExpenseLineDto line, string value)
    {
        if (string.Equals(line.Category, value, StringComparison.Ordinal))
            return;
        line.Category = value;
        var categoryIndex = report?.Lines.IndexOf(line) ?? -1;
        if (categoryIndex >= 0)
            lineCategoryErrors.Remove(categoryIndex);
        if (!ExpenseCategoryRules.IsPersonalCar(value, line.TransportationCode)
            && value != ExpenseCategoryRules.Transportation)
            line.Miles = null;
        if (value != ExpenseCategoryRules.Transportation)
            line.TransportationCode = null;
        if (value != ExpenseCategoryRules.Miscellaneous)
            line.MiscellaneousCode = null;
        if (value != ExpenseCategoryRules.Meals)
        {
            line.MealType = null;
            line.HeadCount = null;
        }
        RecomputeMileage(line);
    }

    private void SetTransportationCode(ExpenseLineDto line, string? value)
    {
        line.TransportationCode = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (!ExpenseCategoryRules.IsPersonalCar(line.Category, line.TransportationCode))
            line.Miles = null;
        RecomputeMileage(line);
    }

    private void SetMiscellaneousCode(ExpenseLineDto line, string? value)
    {
        line.MiscellaneousCode = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private void SetSpent(ExpenseLineDto line, decimal value)
    {
        if (report == null || ExpenseCategoryRules.IsPersonalCar(line.Category, line.TransportationCode))
            return;
        WriteMoney(line, value, line.CurrencyCode, line.ExchangeRateToUsd, line.CurrencyCode);
    }

    private void SetCurrency(ExpenseLineDto line, string value)
    {
        if (report == null || ExpenseCategoryRules.IsPersonalCar(line.Category, line.TransportationCode))
            return;
        WriteMoney(line, line.SpentAmount, value, null, null);
    }

    private void SetMiles(ExpenseLineDto line, decimal? miles)
    {
        line.Miles = miles;
        RecomputeMileage(line);
    }

    private void SetMealType(ExpenseLineDto line, string? value)
    {
        line.MealType = ExpenseMealRules.CleanType(value);
        var index = report?.Lines.IndexOf(line) ?? -1;
        if (index >= 0 && line.MealType != null)
            lineMealTypeErrors.Remove(index);
    }

    private void SetHeadCount(ExpenseLineDto line, int? count)
    {
        line.HeadCount = ExpenseMealRules.Clean(count);
        var index = report?.Lines.IndexOf(line) ?? -1;
        if (index >= 0 && line.HeadCount != null)
            lineHeadCountErrors.Remove(index);
    }

    private void MarkIncompleteMeals()
    {
        lineMealTypeErrors.Clear();
        lineHeadCountErrors.Clear();
        if (report == null) return;
        for (var index = 0; index < report.Lines.Count; index++)
        {
            var line = report.Lines[index];
            if (!string.Equals(line.Category, ExpenseCategoryRules.Meals, StringComparison.Ordinal))
                continue;
            if (ExpenseMealRules.CleanType(line.MealType) == null)
                lineMealTypeErrors.Add(index);
            if (ExpenseMealRules.Clean(line.HeadCount) == null)
                lineHeadCountErrors.Add(index);
        }
    }

    private void RecomputeMileage(ExpenseLineDto line)
    {
        if (report == null) return;
        if (!ExpenseCategoryRules.IsPersonalCar(line.Category, line.TransportationCode)) return;
        line.CurrencyCode = ExpenseCurrencyRules.UsdCode;
        line.ExchangeRateToUsd = 1m;
        line.Amount = ExpenseLineRules.ComputeAmount(
            line.Category, line.Miles, line.Amount, report.MileageRateSnapshot, line.TransportationCode);
        line.SpentAmount = line.Amount;
        if (grouped && !ExpenseCurrencyRules.IsUsd(activeCurrency))
            activeCurrency = ExpenseCurrencyRules.UsdCode;
    }

    private void SetChargeToId(string? organizationId)
    {
        if (report == null) return;
        report.ChargeToOrganizationId = string.IsNullOrWhiteSpace(organizationId) ? null : organizationId.Trim();
        if (report.ChargeToOrganizationId == null)
            report.ChargeToName = null;
    }

    private void SetChargeToName(string? organizationName)
    {
        if (report == null) return;
        if (report.ChargeToOrganizationId == null || string.IsNullOrWhiteSpace(organizationName))
        {
            report.ChargeToName = null;
            return;
        }

        var name = organizationName.Trim();
        report.ChargeToName = name.Length <= 100 ? name : name[..100];
    }

    private IReadOnlyList<ExpensePdfTarget> PdfTargets =>
        report == null
            ? []
            : [
                new ExpensePdfTarget
                {
                    ChargeToOrganizationId = report.ChargeToOrganizationId,
                    Name = report.ChargeToName ?? ""
                }
            ];

    private string CategoryLabel(string? category) =>
        contextChoices.FirstOrDefault(choice => string.Equals(choice.Value, category, StringComparison.Ordinal))?.Label
        ?? category
        ?? "";

    private void SetLineDate(ExpenseLineDto line, DateTime? value)
    {
        if (value is null) return;
        var next = ExpenseLineRules.CalendarDate(value.Value);
        if (line.Date.Date == next.Date) return;
        line.Date = next;
        var index = report?.Lines.IndexOf(line) ?? -1;
        if (index >= 0) lineDateErrors.Remove(index);
    }

    private void SetCoverStart(DateTime? value)
    {
        if (value is null || report is null) return;
        var next = ExpenseLineRules.CalendarDate(value.Value);
        report.CoverStart = next;
        ValidateCoverPeriod();
    }

    private void SetCoverEnd(DateTime? value)
    {
        if (value is null || report is null) return;
        var next = ExpenseLineRules.CalendarDate(value.Value);
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

            if (string.IsNullOrWhiteSpace(line.ExpenseLineId))
                line.ExpenseLineId = Guid.NewGuid().ToString("N");

            if (!ExpenseReceiptRules.TryValidateCount(ShownReceipts(line).Count, files.Count, out var countError))
            {
                Snackbar.Add(countError ?? "Couldn't attach those files.", Severity.Warning);
                return;
            }

            if (ExpenseReportRules.IsLineDateOutsideCover(line.Date, report.CoverStart, report.CoverEnd))
            {
                Snackbar.Add(
                    $"Heads up: this line's date ({line.Date:MM/dd/yyyy}) is outside the cover period ({ExpenseReportRules.CoverRangeLabel(report.CoverStart, report.CoverEnd)}).",
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

                pendingReceipts.Add(new PendingReceipt
                {
                    TempId = Guid.NewGuid().ToString("N"),
                    LineId = line.ExpenseLineId ?? "",
                    FileName = file.Name,
                    MimeType = file.ContentType,
                    Bytes = bytes
                });
                attached++;
                if (ExpenseReceiptRules.IsHeic(file.Name, file.ContentType))
                    attachedHeic = true;
            }

            if (attached == files.Count)
            {
                var kept = attached == 1
                    ? "Receipt attached. Save the report to keep it."
                    : $"{attached} receipts attached. Save the report to keep it.";
                Snackbar.Add(kept, Severity.Success);
                if (attachedHeic)
                    Snackbar.Add("HEIC photos are converted to JPEG for the statement when you save.", Severity.Info);
            }
            else if (attached > 0)
                Snackbar.Add($"{attached} attached. Save the report to keep them. {lastError ?? "The rest could not be attached."}", Severity.Warning);
            else
                Snackbar.Add(lastError ?? "Couldn't attach the receipt.", Severity.Error);
        }
        finally
        {
            attachingLine = null;
            receiptPickerKey++;
        }
    }

    private async Task PreviewReceiptAsync(ExpenseReceiptDto receipt)
    {
        try
        {
            var (b64, mime) = await ReceiptBytesAsync(receipt);
            var parameters = new DialogParameters<ExpenseReceiptPreviewDialog>
            {
                { dialog => dialog.FileName, receipt.OriginalFileName },
                { dialog => dialog.Mime, mime },
                { dialog => dialog.Base64, b64 }
            };
            var options = new DialogOptions
            {
                MaxWidth = MaxWidth.Large,
                FullWidth = true,
                CloseButton = true,
                CloseOnEscapeKey = true
            };
            var title = string.IsNullOrWhiteSpace(receipt.OriginalFileName) ? "Receipt" : receipt.OriginalFileName;
            await DialogService.ShowAsync<ExpenseReceiptPreviewDialog>(title, parameters, options);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't preview the receipt.");
        }
    }

    private async Task DownloadReceiptAsync(ExpenseReceiptDto receipt)
    {
        try
        {
            var (b64, mime) = await ReceiptBytesAsync(receipt);
            await Js.InvokeVoidAsync("fileSave.download", b64, mime, receipt.OriginalFileName);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't download the receipt.");
        }
    }

    private async Task<(string Base64, string Mime)> ReceiptBytesAsync(ExpenseReceiptDto receipt)
    {
        var pending = pendingReceipts.FirstOrDefault(item => item.TempId == receipt.ExpenseReceiptId);
        if (pending != null)
        {
            var mime = string.IsNullOrWhiteSpace(pending.MimeType) ? "application/octet-stream" : pending.MimeType;
            return (Convert.ToBase64String(pending.Bytes), mime);
        }

        var bytes = await client.GetByteArrayAsync(Constants.API.Expenses.ReceiptMedia(receipt.ExpenseReceiptId));
        var savedMime = string.IsNullOrWhiteSpace(receipt.MimeType) ? "application/octet-stream" : receipt.MimeType;
        return (Convert.ToBase64String(bytes), savedMime);
    }

    private List<ExpenseReceiptDto> ShownReceipts(ExpenseLineDto line)
    {
        var saved = line.Receipts.Where(receipt => !removedReceiptIds.Contains(receipt.ExpenseReceiptId));
        var pending = pendingReceipts
            .Where(item => item.LineId == line.ExpenseLineId)
            .Select(item => item.ToDto());
        return saved.Concat(pending).ToList();
    }

    private async Task<string?> CommitReceiptDraftAsync()
    {
        if (report == null) return null;

        foreach (var receiptId in removedReceiptIds.ToList())
        {
            var stillThere = report.Lines.Any(line =>
                line.Receipts.Any(receipt => receipt.ExpenseReceiptId == receiptId));
            if (!stillThere)
            {
                removedReceiptIds.Remove(receiptId);
                continue;
            }

            var response = await client.DeleteAsync(Constants.API.Expenses.ReceiptById(receiptId));
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                return ApiErrorBodyRules.Format(
                    await response.Content.ReadAsStringAsync(),
                    "Couldn't remove the receipt.");
            }

            foreach (var line in report.Lines)
                line.Receipts.RemoveAll(receipt => receipt.ExpenseReceiptId == receiptId);
            removedReceiptIds.Remove(receiptId);
        }

        foreach (var pending in pendingReceipts.ToList())
        {
            var line = report.Lines.FirstOrDefault(item => item.ExpenseLineId == pending.LineId);
            if (line == null)
            {
                pendingReceipts.Remove(pending);
                continue;
            }

            var payload = new UploadExpenseReceiptDto
            {
                FileName = pending.FileName,
                MimeType = pending.MimeType,
                ContentBase64 = Convert.ToBase64String(pending.Bytes)
            };
            var response = await client.PostAsJsonAsync(
                Constants.API.Expenses.LineReceipts(report.ExpenseReportId, pending.LineId), payload);
            if (!response.IsSuccessStatusCode)
            {
                return ApiErrorBodyRules.Format(
                    await response.Content.ReadAsStringAsync(),
                    "Couldn't attach the receipt.");
            }

            var saved = await response.Content.ReadFromJsonAsync<ExpenseReceiptDto>();
            if (saved != null)
                line.Receipts.Add(saved);
            pendingReceipts.Remove(pending);
        }

        return null;
    }

    private async Task RemoveReceiptAsync(ExpenseLineDto line, ExpenseReceiptDto receipt)
    {
        var pending = pendingReceipts.FirstOrDefault(item => item.TempId == receipt.ExpenseReceiptId);
        var body = pending != null
            ? $"Remove {receipt.OriginalFileName} from this line?"
            : $"Remove {receipt.OriginalFileName} from this line? It is deleted when you save.";
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Remove receipt?",
            body,
            yesText: "Remove", cancelText: "Cancel");
        if (confirmed != true) return;

        if (pending != null)
        {
            pendingReceipts.Remove(pending);
            return;
        }

        if (line.Receipts.Any(item => item.ExpenseReceiptId == receipt.ExpenseReceiptId))
            removedReceiptIds.Add(receipt.ExpenseReceiptId);
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
        lineCategoryErrors.Clear();
        lineMealTypeErrors.Clear();
        lineHeadCountErrors.Clear();
    }

    private static UpdateExpenseReportDto ToUpdateDto(ExpenseReportDto report) => new()
    {
        CoverStart = report.CoverStart,
        CoverEnd = report.CoverEnd,
        Purpose = report.Purpose,
        ChargeToOrganizationId = report.ChargeToOrganizationId,
        Lines = report.Lines,
        FormCredits = report.FormCredits
    };

    private static string Fingerprint(ExpenseReportDto report) =>
        JsonSerializer.Serialize(new
        {
            CoverStart = report.CoverStart.ToString("yyyy-MM-dd"),
            CoverEnd = report.CoverEnd.ToString("yyyy-MM-dd"),
            Purpose = report.Purpose ?? "",
            ChargeToOrganizationId = report.ChargeToOrganizationId ?? "",
            Lines = report.Lines.Select(l => new
            {
                Date = l.Date.ToString("yyyy-MM-dd"),
                Description = l.Description ?? "",
                Category = l.Category ?? "",
                Amount = decimal.Round(l.Amount, 2, MidpointRounding.AwayFromZero),
                SpentAmount = decimal.Round(l.SpentAmount, 2, MidpointRounding.AwayFromZero),
                CurrencyCode = l.CurrencyCode ?? "",
                ExchangeRateToUsd = l.ExchangeRateToUsd,
                Miles = l.Miles,
                TransportationCode = l.TransportationCode ?? "",
                MiscellaneousCode = l.MiscellaneousCode ?? "",
                l.MealType,
                l.HeadCount,
                l.SortOrder
            }),
            FormCredits = report.FormCredits.Select(credit => new
            {
                credit.ExpenseCreditId,
                Date = credit.Date.ToString("yyyy-MM-dd"),
                credit.Description,
                Amount = decimal.Round(credit.Amount, 2, MidpointRounding.AwayFromZero),
                credit.CurrencyCode,
                credit.ExchangeRateToUsd
            })
        }, FingerprintJson);

    private void ApplyContextPreferences()
    {
        defaultCurrency = ExpenseCurrencyRules.OptionalCode(meta?.DefaultCurrencyCode) ?? "";
        editorView = ExpenseCurrencyRules.NormalizeView(meta?.EditorView);
        currencyLayout = ExpenseCurrencyRules.NormalizeLayout(meta?.CurrencyLayout);
        if (meta?.Currencies.All(row => !string.Equals(row.Code, defaultCurrency, StringComparison.Ordinal)) == true)
            defaultCurrency = CompanyCode;
        expensePrefsReady = true;
    }

    private void NormalizeLoadedMoney()
    {
        if (report == null)
            return;
        foreach (var line in report.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.CurrencyCode))
                line.CurrencyCode = ExpenseCurrencyRules.UsdCode;
            if (line.ExchangeRateToUsd <= 0m)
                line.ExchangeRateToUsd = 1m;
            if (line.SpentAmount == 0m && line.Amount != 0m && ExpenseCurrencyRules.IsUsd(line.CurrencyCode))
                line.SpentAmount = line.Amount;
        }

        report.FormCredits ??= [];
        foreach (var credit in report.FormCredits)
        {
            credit.Date = ExpenseLineRules.CalendarDate(credit.Date);
            if (string.IsNullOrWhiteSpace(credit.CurrencyCode))
                credit.CurrencyCode = CompanyCode;
            if (ExpenseCurrencyRules.IsHome(credit.CurrencyCode, CompanyCode))
                credit.ExchangeRateToUsd = 1m;
            else if (credit.ExchangeRateToUsd <= 0m && !UserEntersRates)
                credit.ExchangeRateToUsd = CatalogRate(credit.CurrencyCode);
        }
    }

    private void WriteMoney(
        ExpenseLineDto line,
        decimal spent,
        string? currency,
        decimal? savedRate,
        string? savedCurrency)
    {
        if (report == null)
            return;
        decimal? entered = null;
        if (UserEntersRates)
        {
            entered = savedRate is > 0m
                ? savedRate
                : savedRate == null ? ExistingRate(currency) : savedRate;
        }
        var money = ExpenseMoneyRules.Apply(
            line.Category,
            line.TransportationCode,
            line.Miles,
            report.MileageRateSnapshot,
            spent,
            currency,
            CatalogRate(currency),
            UserEntersRates ? entered : savedRate,
            UserEntersRates ? currency : savedCurrency,
            CompanyCode,
            UserEntersRates,
            entered);
        line.CurrencyCode = money.CurrencyCode;
        line.ExchangeRateToUsd = money.ExchangeRateToUsd;
        line.SpentAmount = money.SpentAmount;
        line.Amount = money.AmountUsd;
    }

    private decimal CatalogRate(string? code)
    {
        var normalized = ExpenseCurrencyRules.NormalizeCode(code);
        var row = meta?.Currencies.FirstOrDefault(currency =>
            string.Equals(currency.Code, normalized, StringComparison.Ordinal));
        return row?.RateToUsd > 0m ? row.RateToUsd : 1m;
    }

    private string CurrencyColor(string? code)
    {
        var normalized = ExpenseCurrencyRules.NormalizeCode(code);
        var row = meta?.Currencies.FirstOrDefault(currency =>
            string.Equals(currency.Code, normalized, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(row?.Color))
            return row.Color;
        if (ExpenseCurrencyRules.IsUsd(normalized))
            return ExpenseCurrencyRules.DefaultUsdColor;
        if (string.Equals(normalized, ExpenseCurrencyRules.EurCode, StringComparison.Ordinal))
            return ExpenseCurrencyRules.DefaultEurColor;
        return "#546E7A";
    }

    private string CurrencyStyle(string? code)
    {
        var color = CurrencyColor(code);
        return $"--expense-currency:{color};--expense-currency-ink:{ExpenseCurrencyRules.Ink(color)}";
    }

    private bool ShowCurrencyChip(ExpenseLineDto line) =>
        !string.Equals(
            ExpenseCurrencyRules.NormalizeCode(line.CurrencyCode),
            ExpenseCurrencyRules.NormalizeCode(defaultCurrency),
            StringComparison.Ordinal);

    private bool ShowCurrencyPicker(ExpenseLineDto line) =>
        !grouped && !ExpenseCategoryRules.IsPersonalCar(line.Category, line.TransportationCode);

    private IEnumerable<ExpenseCurrencyDto> CurrencyOptions(string? current)
    {
        var rows = meta?.Currencies ?? [];
        var code = ExpenseCurrencyRules.NormalizeCode(current);
        if (rows.Any(row => string.Equals(row.Code, code, StringComparison.Ordinal)))
            return rows;
        return rows.Append(new ExpenseCurrencyDto { Code = code, Name = code, RateToUsd = 1m });
    }

    private IEnumerable<ExpenseLineDto> VisibleLines
    {
        get
        {
            if (report == null)
                yield break;
            if (!grouped)
            {
                foreach (var line in report.Lines)
                    yield return line;
                yield break;
            }

            var code = ActiveTabCode();
            foreach (var line in report.Lines)
            {
                if (string.Equals(ExpenseCurrencyRules.NormalizeCode(line.CurrencyCode), code, StringComparison.Ordinal))
                    yield return line;
            }
        }
    }

    private IReadOnlyList<string> GroupTabCodes
    {
        get
        {
            var codes = new List<string>();
            void Add(string? code)
            {
                var normalized = ExpenseCurrencyRules.NormalizeCode(code);
                if (!codes.Contains(normalized, StringComparer.Ordinal))
                    codes.Add(normalized);
            }

            if (report != null)
            {
                foreach (var line in report.Lines)
                    Add(line.CurrencyCode);
            }

            if (canEdit)
            {
                foreach (var extra in extraTabs)
                    Add(extra);
            }
            if (codes.Count == 0)
                Add(defaultCurrency);
            return codes;
        }
    }

    private string ActiveTabCode()
    {
        var tabs = GroupTabCodes;
        var current = string.IsNullOrWhiteSpace(activeCurrency)
            ? null
            : ExpenseCurrencyRules.NormalizeCode(activeCurrency);
        if (current != null && tabs.Contains(current, StringComparer.Ordinal))
            return current;
        return tabs.Count == 0 ? defaultCurrency : tabs[0];
    }

    private IEnumerable<ExpenseCurrencyDto> CurrenciesNotTabbed =>
        (meta?.Currencies ?? []).Where(currency =>
            !GroupTabCodes.Contains(currency.Code, StringComparer.Ordinal));

    private bool IsFormOpen(ExpenseLineDto line) =>
        !string.IsNullOrEmpty(line.ExpenseLineId)
        && string.Equals(openLineId, line.ExpenseLineId, StringComparison.Ordinal);

    private void ToggleFormLine(ExpenseLineDto line)
    {
        openLineId = IsFormOpen(line) ? null : line.ExpenseLineId;
    }

    private void SelectCurrencyTab(string code) =>
        activeCurrency = ExpenseCurrencyRules.NormalizeCode(code);

    private void AddCurrencyTab(string? code)
    {
        if (!canEdit || string.IsNullOrWhiteSpace(code))
            return;
        var normalized = ExpenseCurrencyRules.NormalizeCode(code);
        extraTabs.Add(normalized);
        activeCurrency = normalized;
        addCurrencyPick = null;
    }

    private bool CanRemoveCurrencyTab(string code)
    {
        if (!canEdit || report == null)
            return false;
        var normalized = ExpenseCurrencyRules.NormalizeCode(code);
        if (!extraTabs.Contains(normalized))
            return false;
        return !report.Lines.Any(line =>
            string.Equals(ExpenseCurrencyRules.NormalizeCode(line.CurrencyCode), normalized, StringComparison.Ordinal));
    }

    private void RemoveCurrencyTab(string code)
    {
        var normalized = ExpenseCurrencyRules.NormalizeCode(code);
        if (!CanRemoveCurrencyTab(normalized))
            return;
        extraTabs.Remove(normalized);
        if (string.Equals(ExpenseCurrencyRules.NormalizeCode(activeCurrency), normalized, StringComparison.Ordinal))
            activeCurrency = null;
    }

    private async Task SetEditorViewAsync(string value)
    {
        var next = ExpenseCurrencyRules.NormalizeView(value);
        if (next == editorView)
            return;
        editorView = next;
        if (!formView)
            openLineId = null;
        await PersistExpensePreferencesAsync();
    }

    private async Task SetCurrencyLayoutAsync(string value)
    {
        var next = ExpenseCurrencyRules.NormalizeLayout(value);
        if (next == currencyLayout)
            return;
        currencyLayout = next;
        await PersistExpensePreferencesAsync();
    }

    private async Task PersistExpensePreferencesAsync()
    {
        if (!expensePrefsReady)
            return;
        try
        {
            await SettingsService.UpdateExpensePreferencesAsync(defaultCurrency, editorView, currencyLayout);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't save the expense view.");
        }
    }

    private bool CreditHasBeenUsed(ExpenseLineDto line)
    {
        if (report == null || string.IsNullOrWhiteSpace(line.ExpenseLineId) || !report.CreditBucketEnabled)
            return false;
        var (sources, pool) = CreditPool();
        return ExpenseCreditRules.HasBeenUsed(line.ExpenseLineId, sources, pool, CompanyCode);
    }

    private (List<ExpenseCreditSource> Sources, List<ExpenseCreditLine> Lines) CreditPool()
    {
        if (report == null)
            return ([], []);
        var sources = report.CreditSources.Select(source => new ExpenseCreditSource
        {
            LineId = source.LineId,
            ReportId = source.ReportId,
            ChargeToOrganizationId = source.ChargeToOrganizationId,
            ReturnDate = source.ReturnDate,
            CurrencyCode = source.CurrencyCode,
            SpentAmount = source.SpentAmount,
            ExchangeRateToUsd = source.ExchangeRateToUsd,
            AmountUsd = source.AmountUsd
        }).ToList();
        var pool = report.OtherCreditLines.Select(ToCreditLine).ToList();
        pool.AddRange(report.Lines.Select(line => new ExpenseCreditLine
        {
            LineId = line.ExpenseLineId ?? "",
            ReportId = report.ExpenseReportId,
            ChargeToOrganizationId = report.ChargeToOrganizationId,
            ReportStatus = report.Status,
            Date = line.Date,
            CurrencyCode = line.CurrencyCode,
            SpentAmount = line.SpentAmount,
            ExchangeRateToUsd = line.ExchangeRateToUsd,
            AmountUsd = line.Amount,
            SortOrder = line.SortOrder,
            IsReturned = line.IsReturned
        }));
        return (sources, pool);
    }

    private static ExpenseCreditLine ToCreditLine(ExpenseCreditLineDto line) => new()
    {
        LineId = line.LineId,
        ReportId = line.ReportId,
        ChargeToOrganizationId = line.ChargeToOrganizationId,
        ReportStatus = line.ReportStatus,
        Date = line.Date,
        CurrencyCode = line.CurrencyCode,
        SpentAmount = line.SpentAmount,
        ExchangeRateToUsd = line.ExchangeRateToUsd,
        AmountUsd = line.AmountUsd,
        SortOrder = line.SortOrder,
        IsReturned = line.IsReturned
    };

    private IEnumerable<(string Label, decimal Amount)> CategoryTotals
    {
        get
        {
            yield return ("Transportation total", CategoryUsd(ExpenseCategoryRules.Transportation, ExpenseCategoryRules.Mileage));
            yield return ("Hotel total", CategoryUsd(ExpenseCategoryRules.Hotel));
            yield return ("Meal total", CategoryUsd(ExpenseCategoryRules.Meals));
            yield return ("Entertainment total", CategoryUsd(ExpenseCategoryRules.Entertainment));
            yield return ("Telephone total", CategoryUsd(ExpenseCategoryRules.Telephone));
            yield return ("Misc total", CategoryUsd(ExpenseCategoryRules.Miscellaneous, ExpenseCategoryRules.Software));
        }
    }

    private decimal CategoryUsd(params string[] categories)
    {
        if (report == null)
            return 0m;
        return report.Lines
            .Where(line => categories.Contains(line.Category, StringComparer.Ordinal))
            .Sum(line => line.Amount);
    }

    private static string FormDollars(ExpenseLineDto line, string category)
    {
        if (!string.Equals(line.Category, category, StringComparison.Ordinal))
            return "";
        return FormEntered(line);
    }

    private static string FormMiles(ExpenseLineDto line)
    {
        if (!ExpenseCategoryRules.IsPersonalCar(line.Category, line.TransportationCode)
            && line.Category != ExpenseCategoryRules.Mileage)
            return "";
        return line.Miles is > 0m ? line.Miles.Value.ToString("0.##") : "";
    }

    private static string FormTransportDollars(ExpenseLineDto line)
    {
        if (line.Category != ExpenseCategoryRules.Transportation && line.Category != ExpenseCategoryRules.Mileage)
            return "";
        return FormEntered(line);
    }

    private static string FormTransportCode(ExpenseLineDto line)
    {
        if (line.Category != ExpenseCategoryRules.Transportation && line.Category != ExpenseCategoryRules.Mileage)
            return "";
        return line.TransportationCode ?? "";
    }

    private static string FormMiscCode(ExpenseLineDto line)
    {
        if (line.Category == ExpenseCategoryRules.Software)
            return ExpenseForm8743Rules.MiscSoftwareCode;
        if (line.Category == ExpenseCategoryRules.Miscellaneous)
            return line.MiscellaneousCode ?? "";
        return "";
    }

    private string FormTotalMoney(params string[] categories)
    {
        if (!grouped)
            return CategoryUsd(categories).ToString("N2");
        var total = VisibleLines
            .Where(line => categories.Contains(line.Category, StringComparer.Ordinal))
            .Sum(line => ExpenseMoneyRules.EnteredAmount(line.SpentAmount, line.Amount, line.CurrencyCode));
        return total.ToString("N2");
    }

    private string FormTotalMiles()
    {
        if (report == null)
            return "";
        var lines = grouped ? VisibleLines : report.Lines;
        var miles = lines.Sum(line =>
            (line.Category == ExpenseCategoryRules.Mileage
                || ExpenseCategoryRules.IsPersonalCar(line.Category, line.TransportationCode))
                ? line.Miles ?? 0m
                : 0m);
        return miles > 0m ? miles.ToString("0.##") : "";
    }

    private static string FormMiscDollars(ExpenseLineDto line)
    {
        if (line.Category != ExpenseCategoryRules.Software && line.Category != ExpenseCategoryRules.Miscellaneous)
            return "";
        return FormEntered(line);
    }

    private static string FormEntered(ExpenseLineDto line) =>
        ExpenseMoneyRules.EnteredAmount(line.SpentAmount, line.Amount, line.CurrencyCode).ToString("N2");

    private static string MealMark(ExpenseLineDto line, string type)
    {
        if (line.Category != ExpenseCategoryRules.Meals
            || !string.Equals(line.MealType, type, StringComparison.Ordinal)
            || line.HeadCount is not int count)
            return "";
        return count.ToString();
    }

    private async Task<DateTime?> AskDateAsync(string label, string title)
    {
        var parameters = new DialogParameters<ExpenseDateDialog>
        {
            { dialog => dialog.Label, label },
            { dialog => dialog.Value, DateTime.Today }
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true, CloseOnEscapeKey = true };
        var dialog = await DialogService.ShowAsync<ExpenseDateDialog>(title, parameters, options);
        var result = await dialog.Result;
        return result is { Canceled: false, Data: DateTime date } ? date : null;
    }

    private async Task OpenLineCreditAsync(ExpenseLineDto line)
    {
        if (report == null || string.IsNullOrWhiteSpace(line.ExpenseLineId) || !canMarkReturn)
            return;
        var entered = ExpenseMoneyRules.EnteredAmount(line.SpentAmount, line.Amount, line.CurrencyCode);
        var draft = await AskCreditAsync(
            line.IsReturned ? "Edit credit" : "Credit Back",
            line.IsReturned && line.ReturnedOn != default ? line.ReturnedOn : DateTime.Today,
            line.CreditDescription ?? "",
            line.CreditAmount is > 0m ? line.CreditAmount.Value : entered,
            string.IsNullOrWhiteSpace(line.CreditCurrencyCode) ? line.CurrencyCode : line.CreditCurrencyCode!,
            capToLine: true,
            maxAmount: entered,
            maxCurrency: line.CurrencyCode,
            lineAmountHome: line.Amount,
            rates: RatesForCredit(line));
        if (draft == null)
            return;
        try
        {
            var response = await client.PostAsJsonAsync(
                Constants.API.Expenses.ReturnLine(report.ExpenseReportId, line.ExpenseLineId),
                new ReturnExpenseLineDto
                {
                    ReturnDate = draft.Date,
                    Description = draft.Description,
                    Amount = draft.Amount,
                    CurrencyCode = draft.CurrencyCode
                });
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(ApiErrorBodyRules.Format(
                    await response.Content.ReadAsStringAsync(),
                    "Couldn't save the credit."), Severity.Error);
                return;
            }

            report = await response.Content.ReadFromJsonAsync<ExpenseReportDto>();
            NormalizeLineDates();
            NormalizeLoadedMoney();
            RememberSaved();
            Snackbar.Add("Credit saved.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't save the credit.");
        }
    }

    private async Task UndoReturnAsync(ExpenseLineDto line)
    {
        if (report == null || string.IsNullOrWhiteSpace(line.ExpenseLineId) || !canMarkReturn)
            return;
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Undo credit?",
            "Remove this credit?",
            yesText: "Undo",
            cancelText: "Cancel");
        if (confirmed != true)
            return;
        try
        {
            var response = await client.DeleteAsync(
                Constants.API.Expenses.ReturnLine(report.ExpenseReportId, line.ExpenseLineId));
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(ApiErrorBodyRules.Format(
                    await response.Content.ReadAsStringAsync(),
                    "Couldn't remove the credit."), Severity.Error);
                return;
            }

            report = await response.Content.ReadFromJsonAsync<ExpenseReportDto>();
            NormalizeLineDates();
            NormalizeLoadedMoney();
            RememberSaved();
            Snackbar.Add("Credit removed.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't remove the credit.");
        }
    }

    private decimal? ExistingRate(string? currency)
    {
        if (report == null)
            return null;
        var code = ExpenseCurrencyRules.NormalizeCode(currency);
        var line = report.Lines.FirstOrDefault(item =>
            !ExpenseCategoryRules.IsPersonalCar(item.Category, item.TransportationCode)
            && string.Equals(ExpenseCurrencyRules.NormalizeCode(item.CurrencyCode), code, StringComparison.Ordinal)
            && item.ExchangeRateToUsd > 0m);
        if (line != null)
            return line.ExchangeRateToUsd;
        var credit = report.FormCredits.FirstOrDefault(item =>
            string.Equals(ExpenseCurrencyRules.NormalizeCode(item.CurrencyCode), code, StringComparison.Ordinal)
            && item.ExchangeRateToUsd > 0m);
        return credit?.ExchangeRateToUsd;
    }

    private decimal RateForNewLine(string? currency)
    {
        if (ExpenseCurrencyRules.IsHome(currency, CompanyCode))
            return 1m;
        var existing = ExistingRate(currency);
        if (existing is > 0m)
            return existing.Value;
        return UserEntersRates ? 0m : CatalogRate(currency);
    }

    private void SetCoverRate(string code, decimal rate)
    {
        if (report == null || !canEdit)
            return;
        var normalized = ExpenseCurrencyRules.NormalizeCode(code);
        var stored = rate > 0m
            ? ExpenseCurrencyRules.NormalizeRate(normalized, rate, CompanyCode)
            : 0m;
        foreach (var line in report.Lines)
        {
            if (ExpenseCategoryRules.IsPersonalCar(line.Category, line.TransportationCode))
                continue;
            if (!string.Equals(ExpenseCurrencyRules.NormalizeCode(line.CurrencyCode), normalized, StringComparison.Ordinal))
                continue;
            WriteMoney(line, line.SpentAmount, line.CurrencyCode, stored, line.CurrencyCode);
        }

        foreach (var credit in report.FormCredits)
        {
            if (!string.Equals(ExpenseCurrencyRules.NormalizeCode(credit.CurrencyCode), normalized, StringComparison.Ordinal))
                continue;
            credit.ExchangeRateToUsd = stored > 0m ? stored : 0m;
        }
    }

    private string CreditLabel(ExpenseLineDto line)
    {
        var amount = line.CreditAmount is > 0m
            ? line.CreditAmount.Value
            : ExpenseMoneyRules.EnteredAmount(line.SpentAmount, line.Amount, line.CurrencyCode);
        var code = string.IsNullOrWhiteSpace(line.CreditCurrencyCode)
            ? line.CurrencyCode
            : line.CreditCurrencyCode;
        return $"Credit {line.ReturnedOn:MM/dd/yyyy} · {amount:N2} {code}";
    }

    private string StartingCreditCurrency()
    {
        var personal = ExpenseCurrencyRules.OptionalCode(meta?.DefaultCurrencyCode);
        if (!string.IsNullOrEmpty(personal)
            && meta?.Currencies.Any(currency =>
                string.Equals(currency.Code, personal, StringComparison.Ordinal) && currency.IsActive) == true)
            return personal;
        return CompanyCode;
    }

    private Dictionary<string, decimal> RatesForCredit(ExpenseLineDto? line)
    {
        var rates = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var currency in meta?.Currencies ?? [])
        {
            if (ExpenseCurrencyRules.IsHome(currency.Code, CompanyCode))
                rates[currency.Code] = 1m;
            else if (UserEntersRates)
                rates[currency.Code] = ExistingRate(currency.Code) ?? currency.RateToUsd;
            else
                rates[currency.Code] = currency.RateToUsd <= 0m ? 1m : currency.RateToUsd;
        }

        if (line != null)
        {
            var code = ExpenseCurrencyRules.NormalizeCode(line.CurrencyCode);
            rates[code] = line.ExchangeRateToUsd > 0m ? line.ExchangeRateToUsd : 1m;
        }

        return rates;
    }

    private async Task<ExpenseCreditDraft?> AskCreditAsync(
        string title,
        DateTime date,
        string description,
        decimal amount,
        string currencyCode,
        bool capToLine,
        decimal maxAmount,
        string maxCurrency,
        decimal lineAmountHome,
        Dictionary<string, decimal> rates)
    {
        var parameters = new DialogParameters<ExpenseCreditDialog>
        {
            { dialog => dialog.DateValue, date },
            { dialog => dialog.Description, description },
            { dialog => dialog.Amount, amount },
            { dialog => dialog.CurrencyCode, currencyCode },
            { dialog => dialog.Currencies, (meta?.Currencies ?? []).ToList() },
            { dialog => dialog.CapToLine, capToLine },
            { dialog => dialog.MaxAmount, maxAmount },
            { dialog => dialog.MaxCurrency, maxCurrency },
            { dialog => dialog.LineAmountHome, lineAmountHome },
            { dialog => dialog.Rates, rates }
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseOnEscapeKey = true };
        var dialog = await DialogService.ShowAsync<ExpenseCreditDialog>(title, parameters, options);
        var result = await dialog.Result;
        return result is { Canceled: false, Data: ExpenseCreditDraft draft } ? draft : null;
    }

    private async Task AddFormCreditAsync()
    {
        if (report == null || !canEdit || !ShowFormCreditEntry)
            return;
        var currency = StartingCreditCurrency();
        var draft = await AskCreditAsync(
            "Add credit",
            DateTime.Today,
            "",
            0m,
            currency,
            capToLine: false,
            maxAmount: 0m,
            maxCurrency: "",
            lineAmountHome: 0m,
            rates: RatesForCredit(null));
        if (draft == null)
            return;
        report.FormCredits.Add(new ExpenseFormCreditDto
        {
            ExpenseCreditId = Guid.NewGuid().ToString("N"),
            Date = draft.Date,
            Description = draft.Description,
            Amount = draft.Amount,
            CurrencyCode = draft.CurrencyCode,
            ExchangeRateToUsd = RateForCredit(draft.CurrencyCode)
        });
    }

    private async Task EditFormCreditAsync(ExpenseFormCreditDto credit)
    {
        if (report == null || !canEdit || !ShowFormCreditEntry)
            return;
        var draft = await AskCreditAsync(
            "Edit credit",
            credit.Date,
            credit.Description,
            credit.Amount,
            credit.CurrencyCode,
            capToLine: false,
            maxAmount: 0m,
            maxCurrency: "",
            lineAmountHome: 0m,
            rates: RatesForCredit(null));
        if (draft == null)
            return;
        credit.Date = draft.Date;
        credit.Description = draft.Description;
        credit.Amount = draft.Amount;
        credit.CurrencyCode = draft.CurrencyCode;
        credit.ExchangeRateToUsd = RateForCredit(draft.CurrencyCode);
    }

    private async Task RemoveFormCreditAsync(ExpenseFormCreditDto credit)
    {
        if (report == null || !canEdit)
            return;
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Remove credit?",
            "Remove this credit?",
            yesText: "Remove",
            cancelText: "Cancel");
        if (confirmed != true)
            return;
        report.FormCredits.Remove(credit);
    }

    private decimal RateForCredit(string? currency)
    {
        if (ExpenseCurrencyRules.IsHome(currency, CompanyCode))
            return 1m;
        var existing = ExistingRate(currency);
        if (UserEntersRates)
            return existing ?? 0m;
        return existing ?? CatalogRate(currency);
    }

    private sealed class PendingReceipt
    {
        public string TempId { get; init; } = "";
        public string LineId { get; init; } = "";
        public string FileName { get; init; } = "";
        public string MimeType { get; init; } = "";
        public byte[] Bytes { get; init; } = [];

        public ExpenseReceiptDto ToDto() => new()
        {
            ExpenseReceiptId = TempId,
            OriginalFileName = FileName,
            MimeType = string.IsNullOrWhiteSpace(MimeType) ? "application/octet-stream" : MimeType,
            SizeBytes = Bytes.Length
        };
    }

}

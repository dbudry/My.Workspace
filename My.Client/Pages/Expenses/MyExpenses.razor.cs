using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;
using My.Client.Components.Expenses;
using My.Client.Extensions;
using My.Shared.Constants;
using My.Shared.Dtos.Expenses;
using My.Shared.Rules;

namespace My.Client.Pages.Expenses;

public partial class MyExpenses
{
    private const string ManagerViewStorageKey = "expenses-manager-view";

    private bool isLoading = true;
    private bool isCreating;
    private bool isCopying;
    private string? copyingId;
    private bool canManage;
    private string? busyId;
    private string _managerView = "my";

    private List<int> myYearOptions =>
        ExpenseListFilterRules.YearChoices(
            rows.SelectMany(r => ExpenseListFilterRules.YearsOnReport(
                r.Status, r.Year, r.CoverStart, r.CoverEnd)));
    private List<ExpenseReportListDto> rows = [];
    private List<ExpenseReportListDto> teamRows = [];
    private HttpClient client = null!;

    private int? _myYear = DateTime.Today.Year;
    private int? _teamYear = DateTime.Today.Year;
    private HashSet<string> _myStatuses = [];
    private HashSet<string> _teamStatuses = [];
    private string _mySearch = "";
    private string _teamSearch = "";
    private string? _myChargeTo;
    private string? _teamChargeTo;

    private int? myYear
    {
        get => _myYear;
        set
        {
            if (_myYear == value) return;
            _myYear = value;
        }
    }

    private int? teamYear
    {
        get => _teamYear;
        set
        {
            if (_teamYear == value) return;
            _teamYear = value;
            if (!string.IsNullOrEmpty(_userFilter) && teamEmployees.All(u => u.UserId != _userFilter))
                _userFilter = null;
        }
    }

    private string? _userFilter;
    private string? userFilter
    {
        get => _userFilter;
        set { if (_userFilter == value) return; _userFilter = value; }
    }

    private IReadOnlyCollection<string> myStatuses
    {
        get => _myStatuses;
        set
        {
            var next = ToStatusSet(value);
            if (next.SetEquals(_myStatuses)) return;
            _myStatuses = next;
        }
    }

    private IReadOnlyCollection<string> teamStatuses
    {
        get => _teamStatuses;
        set
        {
            var next = ToStatusSet(value);
            if (next.SetEquals(_teamStatuses)) return;
            _teamStatuses = next;
        }
    }

    private string mySearch
    {
        get => _mySearch;
        set
        {
            var next = value ?? "";
            if (_mySearch == next) return;
            _mySearch = next;
        }
    }

    private string teamSearch
    {
        get => _teamSearch;
        set
        {
            var next = value ?? "";
            if (_teamSearch == next) return;
            _teamSearch = next;
        }
    }

    private List<ExpenseChargeToChoice> myChargeToOptions =>
        ExpenseListFilterRules.ChargeToChoices(rows);

    private string? myChargeTo
    {
        get => _myChargeTo;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_myChargeTo == next) return;
            _myChargeTo = next;
        }
    }

    private List<ExpenseReportListDto> visibleMyRows =>
        rows.Where(r => MatchesVisibleRow(r, _myYear, _myStatuses, _mySearch, _myChargeTo)).ToList();

    private List<int> teamYearOptions =>
        ExpenseListFilterRules.YearChoices(
            teamRows.SelectMany(r => ExpenseListFilterRules.YearsOnReport(
                r.Status, r.Year, r.CoverStart, r.CoverEnd)));

    private IEnumerable<ExpenseReportListDto> teamRowsForYear =>
        teamRows.Where(r => ExpenseListFilterRules.MatchesListYear(
            r.Status, r.Year, r.CoverStart, r.CoverEnd, _teamYear));

    private List<(string UserId, string Name)> teamEmployees =>
        teamRowsForYear
            .GroupBy(r => r.UserId)
            .Select(g => (
                UserId: g.Key,
                Name: g.Select(r => r.EmployeeName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? g.Key))
            .OrderBy(u => u.Name)
            .ToList();

    private List<ExpenseChargeToChoice> teamChargeToOptions =>
        ExpenseListFilterRules.ChargeToChoices(teamRows);

    private string? teamChargeTo
    {
        get => _teamChargeTo;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_teamChargeTo == next) return;
            _teamChargeTo = next;
        }
    }

    private List<ExpenseReportListDto> visibleTeamRows =>
        teamRowsForYear
            .Where(r => string.IsNullOrEmpty(_userFilter) || r.UserId == _userFilter)
            .Where(r => ExpenseListFilterRules.MatchesStatuses(r.Status, _teamStatuses)
                && ExpenseListFilterRules.MatchesSearch(r, _teamSearch)
                && ExpenseListFilterRules.MatchesChargeTo(r.ChargeToOrganizationId, _teamChargeTo))
            .ToList();

    private static bool MatchesVisibleRow(
        ExpenseReportListDto row,
        int? year,
        IReadOnlyCollection<string> statuses,
        string? search,
        string? chargeToOrganizationId) =>
        ExpenseListFilterRules.MatchesListYear(row.Status, row.Year, row.CoverStart, row.CoverEnd, year)
        && ExpenseListFilterRules.MatchesStatuses(row.Status, statuses)
        && ExpenseListFilterRules.MatchesSearch(row, search)
        && ExpenseListFilterRules.MatchesChargeTo(row.ChargeToOrganizationId, chargeToOrganizationId);

    private bool ShowMyReports => !canManage || _managerView == "my";
    private bool ShowTeamReports => canManage && _managerView == "team";

    private string pageTitle => ShowTeamReports ? "Team expenses" : "My Expenses";

    [CascadingParameter]
    private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

    [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private IJSRuntime Js { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        client = ClientFactory.CreateClient(Constants.API.ClientName);
        var authState = await AuthenticationStateTask;
        canManage = Constants.Roles.HasScopedAccess(
            authState.User, Constants.Scopes.Expenses, Constants.Roles.Manager);
        if (canManage)
        {
            await RestoreManagerViewAsync();
            var query = Navigation.ToAbsoluteUri(Navigation.Uri).Query;
            if (query.Contains("view=team", StringComparison.OrdinalIgnoreCase))
                _managerView = "team";
            else if (query.Contains("view=my", StringComparison.OrdinalIgnoreCase))
                _managerView = "my";
            try
            {
                await Js.InvokeVoidAsync("sessionStorage.setItem", ManagerViewStorageKey, _managerView);
            }
            catch
            {
                // Non-fatal.
            }
        }
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (ShowTeamReports)
        {
            await ReloadTeamAsync();
            return;
        }

        isLoading = true;
        try
        {
            var list = await client.GetFromJsonAsync<List<ExpenseReportListDto>>(Constants.API.Expenses.Reports);
            rows = list ?? [];
            if (_myYear is int selected && !myYearOptions.Contains(selected))
                _myYear = myYearOptions.Count == 0 ? null : myYearOptions[0];
            if (_myChargeTo != null && myChargeToOptions.All(choice => choice.OrganizationId != _myChargeTo))
                _myChargeTo = null;
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't load expense reports.");
        }
        isLoading = false;
    }

    private async Task OnManagerViewChangedAsync(string value)
    {
        if (_managerView == value) return;
        _managerView = value;
        try
        {
            await Js.InvokeVoidAsync("sessionStorage.setItem", ManagerViewStorageKey, value);
        }
        catch
        {
            // Non-fatal — view still switches for this session.
        }

        await LoadAsync();
    }

    private async Task RestoreManagerViewAsync()
    {
        try
        {
            var stored = await Js.InvokeAsync<string?>("sessionStorage.getItem", ManagerViewStorageKey);
            if (stored == "team" || stored == "my")
                _managerView = stored;
        }
        catch
        {
            // Default My.
        }
    }

    private int teamLoadGen;

    private async Task ReloadTeamAsync()
    {
        var gen = Interlocked.Increment(ref teamLoadGen);
        isLoading = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            var url = Constants.API.Expenses.ConstructUrlForTeam(ExpenseDataExtractionRules.StatusAll);
            var list = await client.GetFromJsonAsync<List<ExpenseReportListDto>>(url);
            if (gen != teamLoadGen) return;
            teamRows = list ?? [];
            if (_teamYear is int selected && !teamYearOptions.Contains(selected))
                _teamYear = teamYearOptions.Count == 0 ? null : teamYearOptions[0];
            if (_teamChargeTo != null && teamChargeToOptions.All(choice => choice.OrganizationId != _teamChargeTo))
                _teamChargeTo = null;
        }
        catch (Exception ex)
        {
            if (gen != teamLoadGen) return;
            Snackbar.AddApiError(ex, "Couldn't load team expenses.");
        }
        finally
        {
            if (gen == teamLoadGen)
                isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void OpenData() => Navigation.NavigateTo("expenses/data");

    private async Task SubmitAsync(ExpenseReportListDto row)
    {
        if (!string.IsNullOrEmpty(busyId)) return;

        var blocked = ExpenseReportRules.SubmitBlockedReason(
            row.Status,
            row.LineCount,
            row.ChargeToOrganizationId);
        if (blocked != null)
        {
            await DialogService.ShowMessageBoxAsync("Can't submit", blocked, yesText: "OK");
            return;
        }

        var issues = ExpenseSubmitWarningRules.Issues(row.Lines.Select(line =>
            new ExpenseSubmitWarningRules.Line(
                line.Date, line.Description, line.Amount, line.Category,
                line.MealType, line.HeadCount, line.ReceiptFileNames.Count)));
        var confirmed = await ExpenseSubmitConfirmDialog.AskAsync(
            DialogService, row.CoverStart, row.CoverEnd, issues);
        if (!confirmed) return;

        busyId = row.ExpenseReportId;
        await InvokeAsync(StateHasChanged);
        try
        {
            var response = await client.PostAsync(Constants.API.Expenses.Submit(row.ExpenseReportId), null);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadApiMessageAsync(response, "Couldn't submit the report."), Severity.Error);
                return;
            }

            var submitted = await response.Content.ReadFromJsonAsync<SubmitExpenseReportResultDto>();
            var filed = submitted?.Report;
            var filedLabel = filed == null
                ? "Submitted."
                : $"Filed as {ExpenseReportRules.FilingMonthLabel(filed.Status, filed.Year, filed.Month)}.";
            if (!string.IsNullOrWhiteSpace(submitted?.EmailWarning))
                Snackbar.Add(filedLabel + " " + submitted.EmailWarning, Severity.Warning);
            else
                Snackbar.Add(filedLabel, Severity.Success);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't submit the report.");
        }
        finally
        {
            busyId = null;
        }
    }

    private async Task UnsubmitAsync(ExpenseReportListDto row)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Unsubmit report?",
            $"Unlock {row.EmployeeName}'s report covering {ExpenseReportRules.CoverRangeLabel(row.CoverStart, row.CoverEnd)} so it can be edited?",
            yesText: "Unsubmit", cancelText: "Cancel");
        if (confirmed != true) return;

        busyId = row.ExpenseReportId;
        try
        {
            var response = await client.PostAsync(Constants.API.Expenses.Unsubmit(row.ExpenseReportId), null);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadApiMessageAsync(response, "Couldn't unsubmit the report."), Severity.Error);
                return;
            }

            Snackbar.Add("Unsubmitted.", Severity.Success);
            await ReloadTeamAsync();
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't unsubmit the report.");
        }
        finally
        {
            busyId = null;
        }
    }

    private async Task ReimburseAsync(ExpenseReportListDto row)
    {
        var blocked = ExpenseReportRules.ReimburseBlockedReason(row.Status);
        if (blocked != null)
        {
            Snackbar.Add(blocked, Severity.Warning);
            return;
        }

        var date = await AskPayDateAsync();
        if (date == null) return;

        busyId = row.ExpenseReportId;
        try
        {
            var response = await client.PostAsJsonAsync(
                Constants.API.Expenses.Reimburse(row.ExpenseReportId),
                new ReimburseExpenseReportDto { PaymentDate = date.Value });
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadApiMessageAsync(response, "Couldn't mark the report paid."), Severity.Error);
                return;
            }

            Snackbar.Add(ExpensePayLabelRules.Done, Severity.Success);
            await ReloadTeamAsync();
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't mark the report paid.");
        }
        finally
        {
            busyId = null;
        }
    }

    private async Task UndoReimburseAsync(ExpenseReportListDto row)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            ExpensePayLabelRules.AskUndo,
            $"Return {row.EmployeeName}'s report covering {ExpenseReportRules.CoverRangeLabel(row.CoverStart, row.CoverEnd)} to submitted? Returned lines on it are cleared.",
            yesText: "Undo", cancelText: "Cancel");
        if (confirmed != true) return;

        busyId = row.ExpenseReportId;
        try
        {
            var response = await client.PostAsync(Constants.API.Expenses.UndoReimburse(row.ExpenseReportId), null);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadApiMessageAsync(response, "Couldn't undo payment."), Severity.Error);
                return;
            }

            Snackbar.Add("Returned to submitted.", Severity.Success);
            await ReloadTeamAsync();
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't undo payment.");
        }
        finally
        {
            busyId = null;
        }
    }

    private async Task CopyExpenseAsync(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || isCopying)
            return;

        var row = rows.FirstOrDefault(existing => existing.ExpenseReportId == sourceId);
        var period = row == null
            ? "this expense"
            : $"the expense covering {ExpenseReportRules.CoverRangeLabel(row.CoverStart, row.CoverEnd)}";
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Copy expense?",
            $"Copy {period} into a new draft? Receipts are not copied.",
            yesText: "Copy",
            cancelText: "Cancel");
        if (confirmed != true)
            return;

        isCopying = true;
        copyingId = sourceId;
        string? createdId = null;
        try
        {
            var source = await client.GetFromJsonAsync<ExpenseReportDto>(
                Constants.API.Expenses.ReportById + sourceId);
            if (source == null)
            {
                Snackbar.Add("Couldn't load that expense.", Severity.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(source.ChargeToOrganizationId))
            {
                Snackbar.Add("This expense has no Charge to, so it can't be copied.", Severity.Warning);
                return;
            }

            var create = await client.PostAsJsonAsync(Constants.API.Expenses.Reports, new CreateExpenseReportDto
            {
                CoverStart = source.CoverStart,
                CoverEnd = source.CoverEnd,
                ChargeToOrganizationId = source.ChargeToOrganizationId
            });
            if (!create.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadApiMessageAsync(create, "Couldn't copy the expense."), Severity.Error);
                return;
            }

            var created = await create.Content.ReadFromJsonAsync<ExpenseReportDto>();
            if (created == null || string.IsNullOrWhiteSpace(created.ExpenseReportId))
            {
                Snackbar.Add("Copied, but the response was empty. Refresh and open the new draft.", Severity.Warning);
                await LoadAsync();
                return;
            }

            createdId = created.ExpenseReportId;
            var sort = 0;
            var update = new UpdateExpenseReportDto
            {
                CoverStart = source.CoverStart,
                CoverEnd = source.CoverEnd,
                Purpose = source.Purpose,
                ChargeToOrganizationId = source.ChargeToOrganizationId,
                Lines = source.Lines.Select(line =>
                {
                    line.ExpenseLineId = Guid.NewGuid().ToString("N");
                    line.Receipts = [];
                    line.IsReturned = false;
                    line.ReturnedOn = default;
                    line.CreditAmount = null;
                    line.CreditCurrencyCode = null;
                    line.CreditDescription = null;
                    line.SortOrder = sort++;
                    return line;
                }).ToList(),
                FormCredits = source.FormCredits.Select(credit => new ExpenseFormCreditDto
                {
                    ExpenseCreditId = Guid.NewGuid().ToString("N"),
                    Date = credit.Date,
                    Description = credit.Description,
                    Amount = credit.Amount,
                    CurrencyCode = credit.CurrencyCode,
                    ExchangeRateToUsd = credit.ExchangeRateToUsd
                }).ToList()
            };
            var saved = await client.PutAsJsonAsync(Constants.API.Expenses.ReportById + createdId, update);
            if (!saved.IsSuccessStatusCode)
            {
                await client.DeleteAsync(Constants.API.Expenses.ReportById + createdId);
                createdId = null;
                Snackbar.Add(await ReadApiMessageAsync(saved, "Couldn't copy the expense."), Severity.Error);
                return;
            }

            Snackbar.Add("Copied the expense. Receipts were not copied.", Severity.Success);
            Navigation.NavigateTo($"expenses/{created.ExpenseReportId}");
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(createdId))
                await client.DeleteAsync(Constants.API.Expenses.ReportById + createdId);
            Snackbar.AddApiError(ex, "Couldn't copy the expense.");
        }
        finally
        {
            isCopying = false;
            copyingId = null;
        }
    }

    private async Task CreateAsync()
    {
        var prior = DateTime.Today.AddMonths(-1);
        var (coverStart, coverEnd) = ExpenseReportRules.DefaultCoverPeriod(prior.Year, prior.Month);
        var parameters = new DialogParameters<ExpenseCreateDialog>
        {
            { dialog => dialog.CoverStart, coverStart },
            { dialog => dialog.CoverEnd, coverEnd }
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseOnEscapeKey = true };
        var dialog = await DialogService.ShowAsync<ExpenseCreateDialog>("New expense", parameters, options);
        var result = await dialog.Result;
        if (result is not { Canceled: false, Data: ExpenseCreateRequest request })
            return;

        isCreating = true;
        try
        {
            var dto = new CreateExpenseReportDto
            {
                CoverStart = request.CoverStart,
                CoverEnd = request.CoverEnd,
                ChargeToOrganizationId = request.ChargeToOrganizationId
            };
            var response = await client.PostAsJsonAsync(Constants.API.Expenses.Reports, dto);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadApiMessageAsync(response, "Couldn't create the report."), Severity.Error);
                return;
            }

            var created = await response.Content.ReadFromJsonAsync<ExpenseReportDto>();
            if (created == null)
            {
                Snackbar.Add("Created, but the response was empty. Refresh and open the report.", Severity.Warning);
                await LoadAsync();
                return;
            }

            Navigation.NavigateTo($"expenses/{created.ExpenseReportId}");
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't create the report.");
        }
        finally
        {
            isCreating = false;
        }
    }

    private void OnExpenseRowClick(DataGridRowClickEventArgs<ExpenseReportListDto> args)
    {
        if (args.Item is null) return;
        Open(args.Item.ExpenseReportId);
    }

    private void Open(string id) => Navigation.NavigateTo($"expenses/{id}");

    private static string PdfFileName(ExpenseReportListDto row) =>
        ExpenseDriveNamingRules.StatementFileName(row.CoverStart, row.CoverEnd, row.EmployeeName);

    private static IReadOnlyList<ExpensePdfTarget> PdfTargets(ExpenseReportListDto row) =>
    [
        new ExpensePdfTarget
        {
            ChargeToOrganizationId = row.ChargeToOrganizationId,
            Name = row.ChargeToName ?? ""
        }
    ];

    private async Task DeleteAsync(ExpenseReportListDto row)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Delete draft?",
            $"Delete the draft covering {ExpenseReportRules.CoverRangeLabel(row.CoverStart, row.CoverEnd)}? This cannot be undone.",
            yesText: "Delete", cancelText: "Cancel");
        if (confirmed != true) return;

        busyId = row.ExpenseReportId;
        try
        {
            var response = await client.DeleteAsync(Constants.API.Expenses.ReportById + row.ExpenseReportId);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadApiMessageAsync(response, "Couldn't delete the report."), Severity.Error);
                return;
            }
            rows.RemoveAll(r => r.ExpenseReportId == row.ExpenseReportId);
            Snackbar.Add("Draft deleted.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't delete the report.");
        }
        finally
        {
            busyId = null;
        }
    }

    private static string StatusChipText(IReadOnlyList<string> selected) =>
        selected.Count == 0 ? "All statuses" : string.Join(", ", selected);

    private static string TeamStatusChipText(IReadOnlyList<string> selected) =>
        selected.Count == 0
            ? "All statuses"
            : string.Join(", ", selected.Select(ExpensePayLabelRules.StatusWord));

    private async Task<DateTime?> AskPayDateAsync()
    {
        var parameters = new DialogParameters<ExpenseDateDialog>
        {
            { dialog => dialog.Label, ExpensePayLabelRules.DateLabel },
            { dialog => dialog.Value, DateTime.Today }
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true, CloseOnEscapeKey = true };
        var dialog = await DialogService.ShowAsync<ExpenseDateDialog>(
            ExpensePayLabelRules.AskMark, parameters, options);
        var result = await dialog.Result;
        return result is { Canceled: false, Data: DateTime date } ? date : null;
    }

    private static HashSet<string> ToStatusSet(IEnumerable<string>? value) =>
        value?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static async Task<string> ReadApiMessageAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        return ApiErrorBodyRules.Format(body, fallback);
    }
}

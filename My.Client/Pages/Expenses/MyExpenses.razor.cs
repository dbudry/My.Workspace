using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;
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
    private bool canManage;
    private string? busyId;
    private string _managerView = "my";
    /// <summary>Team year picker: current year back five years so a prior year
    /// remains selectable after the default month filter narrows the result.</summary>
    private readonly List<int> yearOptions = Enumerable.Range(0, 6)
        .Select(i => DateTime.Today.Year - i)
        .ToList();

    private List<int> myYearOptions =>
        ExpenseListFilterRules.YearChoices(rows.Select(r => r.Year));
    private List<ExpenseReportListDto> rows = [];
    private List<ExpenseReportListDto> teamRows = [];
    private List<(string UserId, string Name)> distinctUsers = [];
    private HttpClient client = null!;

    private HashSet<int> _myYears = [DateTime.Today.Year];
    private HashSet<int> _myMonths = [];
    private HashSet<int> _teamYears = [ExpenseListFilterRules.PriorMonth(DateTime.Today).Year];
    private HashSet<int> _teamMonths = [ExpenseListFilterRules.PriorMonth(DateTime.Today).Month];

    private IReadOnlyCollection<int> myYears
    {
        get => _myYears;
        set => _myYears = ToSet(value);
    }

    private IReadOnlyCollection<int> myMonths
    {
        get => _myMonths;
        set => _myMonths = ToSet(value);
    }

    private IReadOnlyCollection<int> teamYears
    {
        get => _teamYears;
        set
        {
            var next = ToSet(value);
            if (next.SetEquals(_teamYears)) return;
            _teamYears = next;
            _ = ReloadTeamAsync();
        }
    }

    private IReadOnlyCollection<int> teamMonths
    {
        get => _teamMonths;
        set
        {
            var next = ToSet(value);
            if (next.SetEquals(_teamMonths)) return;
            _teamMonths = next;
            _ = ReloadTeamAsync();
        }
    }

    private string? _userFilter;
    private string? userFilter
    {
        get => _userFilter;
        set { if (_userFilter == value) return; _userFilter = value; _ = ReloadTeamAsync(); }
    }

    private List<ExpenseReportListDto> visibleMyRows =>
        rows.Where(r => ExpenseListFilterRules.MatchesPeriod(r.Year, r.Month, _myYears, _myMonths)).ToList();

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
            _myYears.IntersectWith(myYearOptions);
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
            var url = Constants.API.Expenses.ConstructUrlForTeam(
                ExpenseDataExtractionRules.StatusAll,
                userFilter,
                years: _teamYears,
                months: _teamMonths);
            var list = await client.GetFromJsonAsync<List<ExpenseReportListDto>>(url);
            if (gen != teamLoadGen) return;
            teamRows = list ?? [];
            if (string.IsNullOrEmpty(userFilter))
            {
                distinctUsers = teamRows
                    .GroupBy(r => r.UserId)
                    .Select(g => (UserId: g.Key, Name: g.First().EmployeeName))
                    .OrderBy(u => u.Name)
                    .ToList();
            }
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
        var blocked = ExpenseReportRules.SubmitBlockedReason(row.Status, row.LineCount);
        if (blocked != null)
        {
            Snackbar.Add(blocked, Severity.Warning);
            return;
        }

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Submit report?",
            $"Submit {MonthLabel(row.Year, row.Month)}? This locks the report and files the statement PDF on Drive.",
            yesText: "Submit", cancelText: "Cancel");
        if (confirmed != true) return;

        busyId = row.ExpenseReportId;
        try
        {
            var response = await client.PostAsync(Constants.API.Expenses.Submit(row.ExpenseReportId), null);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadApiMessageAsync(response, "Couldn't submit the report."), Severity.Error);
                return;
            }

            Snackbar.Add("Submitted.", Severity.Success);
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
            $"Unlock {row.EmployeeName}'s {MonthLabel(row.Year, row.Month)} report so it can be edited?",
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

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Mark reimbursed?",
            $"Mark {row.EmployeeName}'s {MonthLabel(row.Year, row.Month)} report as reimbursed?",
            yesText: "Reimburse", cancelText: "Cancel");
        if (confirmed != true) return;

        busyId = row.ExpenseReportId;
        try
        {
            var response = await client.PostAsync(Constants.API.Expenses.Reimburse(row.ExpenseReportId), null);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadApiMessageAsync(response, "Couldn't mark the report reimbursed."), Severity.Error);
                return;
            }

            Snackbar.Add("Reimbursed.", Severity.Success);
            await ReloadTeamAsync();
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't mark the report reimbursed.");
        }
        finally
        {
            busyId = null;
        }
    }

    private async Task UndoReimburseAsync(ExpenseReportListDto row)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Undo reimbursed?",
            $"Return {row.EmployeeName}'s {MonthLabel(row.Year, row.Month)} report to submitted?",
            yesText: "Undo", cancelText: "Cancel");
        if (confirmed != true) return;

        busyId = row.ExpenseReportId;
        try
        {
            var response = await client.PostAsync(Constants.API.Expenses.UndoReimburse(row.ExpenseReportId), null);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadApiMessageAsync(response, "Couldn't undo reimbursed."), Severity.Error);
                return;
            }

            Snackbar.Add("Returned to submitted.", Severity.Success);
            await ReloadTeamAsync();
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't undo reimbursed.");
        }
        finally
        {
            busyId = null;
        }
    }

    private async Task CreateAsync()
    {
        isCreating = true;
        try
        {
            var (year, month) = ExpenseListFilterRules.CreatePeriod(_myYears, _myMonths, DateTime.Today);
            var dto = new CreateExpenseReportDto
            {
                Year = year,
                Month = month
            };
            var response = await client.PostAsJsonAsync(Constants.API.Expenses.Reports, dto);
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                Snackbar.Add("You already have a report for that month.", Severity.Warning);
                return;
            }
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

    private static string PdfFileName(ExpenseReportListDto row)
    {
        var who = string.Join("_", (row.EmployeeName ?? "Employee")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return $"{row.Year}_{row.Month:00}_{who}_Expenses.pdf";
    }

    private async Task DeleteAsync(ExpenseReportListDto row)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Delete draft?",
            $"Delete the {MonthLabel(row.Year, row.Month)} draft? This cannot be undone.",
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

    private static string MonthLabel(int year, int month) =>
        new DateTime(year, month, 1).ToString("MMMM yyyy");

    private static HashSet<int> ToSet(IEnumerable<int>? value) =>
        value?.ToHashSet() ?? [];

    private static string YearChipText(IReadOnlyList<string> selected) =>
        selected.Count == 0 ? "All years" : string.Join(", ", selected.OrderByDescending(s => s));

    private static string MonthChipText(IReadOnlyList<string> selected)
    {
        if (selected.Count is 0 or 12)
            return "All months";
        return string.Join(", ", selected
            .Select(s => int.TryParse(s, out var m) ? m : 0)
            .Where(m => m is >= 1 and <= 12)
            .OrderBy(m => m)
            .Select(m => new DateTime(2000, m, 1).ToString("MMM")));
    }

    private static async Task<string> ReadApiMessageAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        return ApiErrorBodyRules.Format(body, fallback);
    }
}

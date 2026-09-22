using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MiniExcelLibs;
using MudBlazor;
using My.Client.Extensions;
using My.Shared.Constants;
using My.Shared.Dtos.Expenses;
using My.Shared.Rules;

namespace My.Client.Pages.Expenses;

public partial class ExpenseData
{
    private bool isLoading = true;
    private bool isPreviewing;
    private bool isExporting;
    private string? validationMessage;
    private ExpenseDataExportDto preview = new();
    private bool previewLoaded;

    private string _statusFilter = ExpenseDataExtractionRules.StatusSubmitted;
    private string statusFilter
    {
        get => _statusFilter;
        set { if (_statusFilter == value) return; _statusFilter = value; InvalidatePreview(); }
    }
    private int? _yearFilter = DateTime.Today.Year;
    private int? yearFilter
    {
        get => _yearFilter;
        set { if (_yearFilter == value) return; _yearFilter = value; InvalidatePreview(); }
    }
    private int? _monthFilter;
    private int? monthFilter
    {
        get => _monthFilter;
        set { if (_monthFilter == value) return; _monthFilter = value; InvalidatePreview(); }
    }
    private string? _userFilter;
    private string? userFilter
    {
        get => _userFilter;
        set { if (_userFilter == value) return; _userFilter = value; InvalidatePreview(); }
    }
    private readonly HashSet<string> selectedEntities = new(StringComparer.Ordinal)
    {
        ExpenseDataExtractionRules.Reports,
        ExpenseDataExtractionRules.Lines,
        ExpenseDataExtractionRules.Receipts
    };
    private readonly List<int> yearOptions = Enumerable.Range(0, 6)
        .Select(i => DateTime.Today.Year - i)
        .ToList();
    private List<(string UserId, string Name)> employeeOptions = [];
    private HttpClient client = null!;

    private static readonly (string Key, string Label)[] entityChoices =
    [
        (ExpenseDataExtractionRules.Reports, "Reports"),
        (ExpenseDataExtractionRules.Lines, "Line items"),
        (ExpenseDataExtractionRules.Receipts, "Receipt metadata")
    ];

    [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IJSRuntime Js { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        client = ClientFactory.CreateClient(Constants.API.ClientName);
        try
        {
            var url = Constants.API.Expenses.ConstructUrlForTeam(ExpenseDataExtractionRules.StatusAll);
            var list = await client.GetFromJsonAsync<List<ExpenseReportListDto>>(url);
            employeeOptions = (list ?? [])
                .GroupBy(r => r.UserId)
                .Select(g => (UserId: g.Key, Name: g.First().EmployeeName))
                .OrderBy(u => u.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't load employees.");
        }

        isLoading = false;
    }

    private void ToggleEntity(string key, bool selected)
    {
        if (selected)
            selectedEntities.Add(key);
        else
            selectedEntities.Remove(key);
        InvalidatePreview();
    }

    private void InvalidatePreview()
    {
        if (!previewLoaded && !isPreviewing)
            return;
        preview = new();
        previewLoaded = false;
        validationMessage = null;
    }

    private int PreviewCount(string entity) => entity switch
    {
        ExpenseDataExtractionRules.Reports => preview.Reports.Count,
        ExpenseDataExtractionRules.Lines => preview.Lines.Count,
        ExpenseDataExtractionRules.Receipts => preview.Receipts.Count,
        _ => 0
    };

    private bool previewIsEmpty =>
        selectedEntities.All(e => PreviewCount(e) == 0);

    private string previewSummary
    {
        get
        {
            var parts = new List<string>();
            if (selectedEntities.Contains(ExpenseDataExtractionRules.Reports))
                parts.Add($"{preview.Reports.Count} report{(preview.Reports.Count == 1 ? "" : "s")}");
            if (selectedEntities.Contains(ExpenseDataExtractionRules.Lines))
                parts.Add($"{preview.Lines.Count} line item{(preview.Lines.Count == 1 ? "" : "s")}");
            if (selectedEntities.Contains(ExpenseDataExtractionRules.Receipts))
                parts.Add($"{preview.Receipts.Count} receipt{(preview.Receipts.Count == 1 ? "" : "s")}");
            return parts.Count == 0 ? "" : string.Join(" · ", parts);
        }
    }

    private IEnumerable<ExpenseReportExportRow> previewReports =>
        preview.Reports
            .OrderBy(r => r.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Year).ThenBy(r => r.Month);

    private IReadOnlyList<IGrouping<string, ExpenseLineExportRow>> groupedLines =>
        preview.Lines
            .OrderBy(l => l.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.Year).ThenBy(l => l.Month).ThenBy(l => l.Date)
            .GroupBy(l => EmployeeKey(l.EmployeeName))
            .ToList();

    private IReadOnlyList<IGrouping<string, ExpenseReceiptExportRow>> groupedReceipts =>
        preview.Receipts
            .OrderBy(r => r.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.OriginalFileName, StringComparer.OrdinalIgnoreCase)
            .GroupBy(r => EmployeeKey(r.EmployeeName))
            .ToList();

    private async Task LoadPreviewAsync()
    {
        validationMessage = ExpenseDataExtractionRules.ValidateRequest(selectedEntities, yearFilter, monthFilter);
        if (validationMessage is not null)
        {
            preview = new();
            previewLoaded = false;
            return;
        }
        if (selectedEntities.Count == 0)
        {
            validationMessage = "Select at least one dataset.";
            preview = new();
            previewLoaded = false;
            return;
        }

        isPreviewing = true;
        StateHasChanged();
        try
        {
            var userIds = string.IsNullOrWhiteSpace(userFilter) ? null : new[] { userFilter };
            var url = Constants.API.Expenses.ConstructUrlForData(
                selectedEntities,
                statusFilter,
                yearFilter,
                monthFilter,
                userIds);

            var response = await client.GetAsync($"{client.BaseAddress}{url}");
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                validationMessage = string.IsNullOrWhiteSpace(error) ? "Couldn't load preview." : error.Trim('"');
                preview = new();
                previewLoaded = false;
                return;
            }

            preview = await response.Content.ReadFromJsonAsync<ExpenseDataExportDto>()
                ?? new ExpenseDataExportDto();
            previewLoaded = true;
            validationMessage = null;
        }
        catch (Exception ex)
        {
            preview = new();
            previewLoaded = false;
            Snackbar.AddApiError(ex, "Couldn't load expense data.");
        }
        finally
        {
            isPreviewing = false;
            StateHasChanged();
        }
    }

    private async Task ExportExcelAsync()
    {
        if (!previewLoaded)
        {
            validationMessage = "Get data first.";
            return;
        }
        if (previewIsEmpty)
        {
            validationMessage = "No matching rows.";
            Snackbar.Add("Nothing to export.", Severity.Warning);
            return;
        }

        isExporting = true;
        try
        {
            var sheets = new Dictionary<string, object>();
            if (selectedEntities.Contains(ExpenseDataExtractionRules.Reports))
                sheets["Reports"] = preview.Reports;
            if (selectedEntities.Contains(ExpenseDataExtractionRules.Lines))
                sheets["Lines"] = preview.Lines;
            if (selectedEntities.Contains(ExpenseDataExtractionRules.Receipts))
                sheets["Receipts"] = preview.Receipts;

            using var stream = new MemoryStream();
            await stream.SaveAsAsync(sheets);

            var base64 = Convert.ToBase64String(stream.ToArray());
            var fileName = $"ExpenseData_{DateTime.Today:yyyyMMdd}.xlsx";

            await Js.InvokeVoidAsync("eval",
                $"var a=document.createElement('a');" +
                $"a.href='data:application/vnd.openxmlformats-officedocument.spreadsheetml.sheet;base64,{base64}';" +
                $"a.download='{fileName}';" +
                $"document.body.appendChild(a);a.click();document.body.removeChild(a);");

            Snackbar.Add("Export ready.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't export expenses.");
        }
        finally
        {
            isExporting = false;
        }
    }

    private static string EmployeeKey(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "(No name)" : name.Trim();

    private static string MonthLabel(int year, int month) =>
        new DateTime(year, month, 1).ToString("MMM yyyy");

    private static string FileSizeLabel(int bytes) =>
        bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024.0:0.#} KB";
}

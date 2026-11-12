using Microsoft.AspNetCore.Components;
using MudBlazor;
using My.Client.Services;
using My.Shared.Dtos.Crm;
using My.Shared.Dtos.Paging;

namespace My.Client.Pages.Crm;

public partial class Report
{
    [Inject] private CrmService Crm { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private MudTable<CrmReportRowDto>? table;
    private List<CrmStageDto> stages = new();
    private string? stageFilter;
    private string? search;
    private DateTime? fromDate;
    private DateTime? toDate;

    protected override async Task OnInitializedAsync()
    {
        stages = await Crm.StagesAsync() ?? new();
    }

    private async Task<TableData<CrmReportRowDto>> LoadServerData(TableState state, CancellationToken cancellationToken)
    {
        var response = await Crm.ReportAsync(new ListQueryParameters
        {
            PageNumber = state.Page + 1,
            PageSize = state.PageSize,
            Search = search
        }, stageFilter, fromDate, toDate);
        return new TableData<CrmReportRowDto>
        {
            Items = response?.Items ?? new List<CrmReportRowDto>(),
            TotalItems = response?.TotalCount ?? 0
        };
    }

    private async Task OnStageChanged(string? value)
    {
        stageFilter = value;
        await ReloadAsync();
    }

    private Task OnFromChanged(DateTime? value)
    {
        fromDate = value;
        return ReloadAsync();
    }

    private Task OnToChanged(DateTime? value)
    {
        toDate = value;
        return ReloadAsync();
    }

    private Task ReloadAsync() => table?.ReloadServerData() ?? Task.CompletedTask;

    private void Open(CrmReportRowDto? row)
    {
        if (row == null)
            return;
        Navigation.NavigateTo($"crm/potential/{row.PotentialId}");
    }
}

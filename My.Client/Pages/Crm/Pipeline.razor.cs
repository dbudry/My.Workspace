using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.Crm;
using My.Shared.Dtos.Paging;

namespace My.Client.Pages.Crm;

public partial class Pipeline
{
    [Inject] private CrmService Crm { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    private List<PotentialListItemDto> items = new();
    private List<CrmStageDto> stages = new();
    private string? stageFilter;
    private string? search;
    private bool includeArchived;
    private bool loading = true;
    private bool canEdit;

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthStateTask).User;
        canEdit = Constants.Roles.HasScopedAccess(user, Constants.Scopes.Crm, Constants.Roles.Editor);
        stages = await Crm.StagesAsync() ?? new();
        await ReloadAsync();
    }

    private async Task OnStageChanged(string? value)
    {
        stageFilter = value;
        await ReloadAsync();
    }

    private async Task OnIncludeArchivedChanged(bool value)
    {
        includeArchived = value;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        loading = true;
        try
        {
            var response = await Crm.ListAsync(new ListQueryParameters
            {
                PageNumber = 1,
                PageSize = ListQueryParameters.MaxPageSize,
                Search = search,
                IncludeArchived = includeArchived
            }, stageFilter);
            items = response?.Items?.ToList() ?? new List<PotentialListItemDto>();
        }
        finally
        {
            loading = false;
        }
    }

    private void Open(PotentialListItemDto? item)
    {
        if (item == null)
            return;
        Navigation.NavigateTo($"crm/potential/{item.PotentialId}");
    }
}

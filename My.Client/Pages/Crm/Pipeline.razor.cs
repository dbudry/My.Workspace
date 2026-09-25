using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using My.Client.Components.Crm;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.Crm;
using My.Shared.Dtos.Paging;
using My.Shared.Rules;

namespace My.Client.Pages.Crm;

public partial class Pipeline
{
    [Inject] private CrmService Crm { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    private List<OpportunityListItemDto> items = new();
    private string? stageFilter;
    private string? search;
    private bool includeArchived;
    private bool loading = true;
    private bool canEdit;
    private bool canManage;

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthStateTask).User;
        canEdit = Constants.Roles.HasScopedAccess(user, Constants.Scopes.Crm, Constants.Roles.Editor);
        canManage = Constants.Roles.HasScopedAccess(user, Constants.Scopes.Crm, Constants.Roles.Manager);
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
            items = response?.Items?.ToList() ?? new List<OpportunityListItemDto>();
        }
        finally
        {
            loading = false;
        }
    }

    private Task CreateAsync() => OpenAsync(null);

    private async Task OpenAsync(string? opportunityId)
    {
        var parameters = new DialogParameters<OpportunityDialog>
        {
            { x => x.OpportunityId, opportunityId },
            { x => x.CanEdit, canEdit },
            { x => x.CanManage, canManage }
        };
        var dialog = await DialogService.ShowAsync<OpportunityDialog>(
            opportunityId == null ? "New opportunity" : "Opportunity",
            parameters,
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
        var result = await dialog.Result;
        if (result is { Canceled: false })
            await ReloadAsync();
    }
}

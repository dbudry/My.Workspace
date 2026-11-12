using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using My.Client.Extensions;
using My.Client.Services;
using My.Shared.Dtos.Crm;
using My.Shared.Rules;

namespace My.Client.Pages.Crm;

public partial class Settings
{
    [Inject] private CrmService Crm { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;

    private bool loading = true;
    private bool busy;
    private List<CrmStageDto> stages = new();
    private List<CrmRelationshipTypeDto> relationshipTypes = new();
    private List<CrmNotifyUserDto> notifyUsers = new();
    private string newStageName = "";
    private int newStagePercent;
    private string newStageOutcome = CrmStageRules.Open;
    private string newTypeName = "";
    private string newTypeDescription = "";

    protected override async Task OnInitializedAsync()
    {
        await ReloadAsync();
        loading = false;
    }

    private async Task ReloadAsync()
    {
        stages = await Crm.StagesAsync() ?? new();
        relationshipTypes = await Crm.RelationshipTypesAsync() ?? new();
        notifyUsers = await Crm.NotifyUsersAsync() ?? new();
    }

    private async Task SaveNotifyUsersAsync()
    {
        busy = true;
        try
        {
            var response = await Crm.SaveNotifyUsersAsync(notifyUsers.Where(user => user.Selected).Select(user => user.UserId));
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadMessage(response, "Couldn't save discussion email recipients."), Severity.Error);
                notifyUsers = await Crm.NotifyUsersAsync() ?? notifyUsers;
                return;
            }

            notifyUsers = await response.Content.ReadFromJsonAsync<List<CrmNotifyUserDto>>() ?? notifyUsers;
            Snackbar.Add("Discussion email recipients saved.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't save discussion email recipients.");
        }
        finally
        {
            busy = false;
        }
    }

    private static bool IsDecisionMaker(CrmRelationshipTypeDto type) =>
        string.Equals(type.Name?.Trim(), CrmRelationshipTypeRules.DecisionMaker, StringComparison.OrdinalIgnoreCase);

    private async Task SaveStageAsync(CrmStageDto stage)
    {
        busy = true;
        try
        {
            var response = await Crm.UpdateStageAsync(new UpdateCrmStageDto
            {
                CrmStageId = stage.CrmStageId,
                Name = stage.Name,
                DefaultProbability = stage.DefaultProbability,
                Outcome = stage.Outcome
            });
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadMessage(response, "Couldn't save the stage."), Severity.Error);
                return;
            }

            Snackbar.Add("Stage saved.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't save the stage.");
        }
        finally
        {
            busy = false;
        }
    }

    private async Task AddStageAsync()
    {
        if (string.IsNullOrWhiteSpace(newStageName))
        {
            Snackbar.Add("Stage name is required.", Severity.Warning);
            return;
        }

        busy = true;
        try
        {
            var response = await Crm.CreateStageAsync(new CreateCrmStageDto
            {
                Name = newStageName.Trim(),
                DefaultProbability = newStagePercent,
                Outcome = newStageOutcome
            });
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadMessage(response, "Couldn't add the stage."), Severity.Error);
                return;
            }

            var created = await response.Content.ReadFromJsonAsync<CrmStageDto>();
            if (created != null)
                stages.Add(created);
            newStageName = "";
            newStagePercent = 0;
            newStageOutcome = CrmStageRules.Open;
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't add the stage.");
        }
        finally
        {
            busy = false;
        }
    }

    private async Task DeleteStageAsync(CrmStageDto stage)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Confirm Delete",
            $"Delete stage \"{stage.Name}\"?",
            yesText: "Delete",
            cancelText: "Cancel");
        if (confirmed != true)
            return;

        busy = true;
        try
        {
            var response = await Crm.DeleteStageAsync(stage.CrmStageId);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadMessage(response, "Couldn't delete the stage."), Severity.Error);
                return;
            }

            stages.Remove(stage);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't delete the stage.");
        }
        finally
        {
            busy = false;
        }
    }

    private async Task MoveStageAsync(CrmStageDto stage, int direction)
    {
        var index = stages.IndexOf(stage);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= stages.Count)
            return;
        (stages[index], stages[target]) = (stages[target], stages[index]);
        busy = true;
        try
        {
            var response = await Crm.ReorderStagesAsync(stages.Select(item => item.CrmStageId).ToList());
            if (!response.IsSuccessStatusCode)
            {
                (stages[index], stages[target]) = (stages[target], stages[index]);
                Snackbar.Add(await ReadMessage(response, "Couldn't reorder the stages."), Severity.Error);
            }
        }
        catch (Exception ex)
        {
            (stages[index], stages[target]) = (stages[target], stages[index]);
            Snackbar.AddApiError(ex, "Couldn't reorder the stages.");
        }
        finally
        {
            busy = false;
        }
    }

    private async Task AddTypeAsync()
    {
        if (string.IsNullOrWhiteSpace(newTypeName))
        {
            Snackbar.Add("Name is required.", Severity.Warning);
            return;
        }

        relationshipTypes.Add(new CrmRelationshipTypeDto
        {
            Name = newTypeName.Trim(),
            Description = newTypeDescription.Trim()
        });
        newTypeName = "";
        newTypeDescription = "";
        await SaveTypesAsync();
    }

    private async Task RemoveTypeAsync(CrmRelationshipTypeDto type)
    {
        relationshipTypes.Remove(type);
        await SaveTypesAsync();
    }

    private async Task SaveTypesAsync()
    {
        busy = true;
        try
        {
            var response = await Crm.SaveRelationshipTypesAsync(relationshipTypes);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.Add(await ReadMessage(response, "Couldn't save relationship types."), Severity.Error);
                relationshipTypes = await Crm.RelationshipTypesAsync() ?? relationshipTypes;
                return;
            }

            relationshipTypes = await response.Content.ReadFromJsonAsync<List<CrmRelationshipTypeDto>>() ?? relationshipTypes;
            Snackbar.Add("Relationship types saved.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't save relationship types.");
        }
        finally
        {
            busy = false;
        }
    }

    private static async Task<string> ReadMessage(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        return ApiErrorBodyRules.Format(body, fallback);
    }
}

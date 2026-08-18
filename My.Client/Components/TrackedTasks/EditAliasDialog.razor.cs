using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using My.Client.Extensions;
using My.Client.Models;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.TrackedTaskAlias;

namespace My.Client.Components.TrackedTasks
{
    public partial class EditAliasDialog
    {
        [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

        [Parameter] public string TaskId { get; set; } = null!;
        [Parameter] public string InitialName { get; set; } = null!;
        [Parameter] public string OriginalUserName { get; set; } = string.Empty;
        [Parameter] public DateTime InitialStartDate { get; set; }
        [Parameter] public TimeSpan InitialDuration { get; set; }
        [Parameter] public string? InitialProjectId { get; set; }
        [Parameter] public bool InitialIsBillable { get; set; }
        [Parameter] public bool IsExisting { get; set; }

        [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
        [Inject] private ISnackbar Snackbar { get; set; } = null!;
        [Inject] private ProjectsCache ProjectsCache { get; set; } = null!;

        private string editName = string.Empty;
        private DateTime? editDate;
        private int editHours;
        private int editMinutes;
        private Project? selectedProject;
        private bool editIsBillable;
        private string? saveError;
        private bool isBusy;

        protected override async Task OnInitializedAsync()
        {
            editName = InitialName;
            editDate = InitialStartDate.ToLocalTime().Date;
            editHours = (int)InitialDuration.TotalHours;
            editMinutes = InitialDuration.Minutes;
            editIsBillable = InitialIsBillable;

            try
            {
                if (!string.IsNullOrEmpty(InitialProjectId))
                    selectedProject = await ProjectsCache.ResolveByIdAsync(InitialProjectId);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load projects.");
            }
        }

        /// <summary>
        /// Server-backed search — same pattern as Tasks/Reports. The old in-memory pool
        /// was capped at the first 25 projects and left managers with an empty picker.
        /// </summary>
        private async Task<IEnumerable<Project>> SearchProjects(string? query, CancellationToken token)
        {
            try
            {
                // Even managers can only alias to an active, non-archived project — server enforces.
                var results = await ProjectsCache.LookupActiveAsync(search: query);
                return results.Where(p => p.IsActive && !p.IsArchived);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't search projects.");
                return Enumerable.Empty<Project>();
            }
        }

        private void OnProjectChanged(Project? project) => selectedProject = project;

        private async Task SaveAsync()
        {
            saveError = null;
            if (string.IsNullOrWhiteSpace(editName) || editName.Trim().Length < 2)
            {
                saveError = "Description must be at least 2 characters.";
                return;
            }
            if (editDate == null)
            {
                saveError = "Date is required.";
                return;
            }
            if (editHours == 0 && editMinutes == 0)
            {
                saveError = "Duration must be greater than zero.";
                return;
            }

            isBusy = true;
            try
            {
                var client = ClientFactory.CreateClient(Constants.API.ClientName);
                var payload = new UpsertTrackedTaskAliasDto
                {
                    Details = editName.Trim(),
                    StartDate = DateTime.SpecifyKind(editDate.Value, DateTimeKind.Local).ToUniversalTime(),
                    Duration = new TimeSpan(editHours, editMinutes, 0),
                    ProjectId = selectedProject?.ProjectId,
                    IsBillable = editIsBillable
                };

                var resp = await client.PutAsJsonAsync($"{Constants.API.TrackedTaskAlias.Upsert}{TaskId}", payload);
                if (!resp.IsSuccessStatusCode)
                {
                    saveError = await resp.Content.ReadAsStringAsync();
                    return;
                }

                var saved = await resp.Content.ReadFromJsonAsync<TrackedTaskAliasDto>();
                MudDialog.Close(DialogResult.Ok(saved));
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't save alias.");
            }
            finally
            {
                isBusy = false;
            }
        }

        private void Cancel() => MudDialog.Cancel();
    }
}

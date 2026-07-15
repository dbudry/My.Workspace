using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using My.Client.Extensions;
using My.Client.Models;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.TrackedTask;

namespace My.Client.Components.TrackedTasks
{
    public partial class EditDirectDialog
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
        private IReadOnlyList<Project> allProjects = Array.Empty<Project>();

        protected override async Task OnInitializedAsync()
        {
            editName = InitialName;
            editDate = InitialStartDate.ToLocalTime().Date;
            editHours = (int)InitialDuration.TotalHours;
            editMinutes = InitialDuration.Minutes;
            editIsBillable = InitialIsBillable;

            try
            {
                allProjects = await ProjectsCache.LookupAsync();
                if (!string.IsNullOrEmpty(InitialProjectId))
                    selectedProject = allProjects.FirstOrDefault(p => p.ProjectId == InitialProjectId);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load projects.");
            }
        }

        private Task<IEnumerable<Project>> SearchProjects(string query, CancellationToken token)
        {
            var pool = allProjects.Where(p => p.IsActive && !p.IsArchived);
            if (string.IsNullOrEmpty(query))
                return Task.FromResult<IEnumerable<Project>>(pool.Take(20));
            return Task.FromResult<IEnumerable<Project>>(
                pool.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(20));
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
                var payload = new ManagerTimeCorrectionDto
                {
                    Name = editName.Trim(),
                    StartDate = DateTime.SpecifyKind(editDate.Value, DateTimeKind.Local).ToUniversalTime(),
                    Duration = new TimeSpan(editHours, editMinutes, 0),
                    ProjectId = selectedProject?.ProjectId,
                    IsBillable = editIsBillable
                };

                var resp = await client.PutAsJsonAsync(
                    $"{Constants.API.TrackedTask.ManagerCorrection}{TaskId}/manager-correction", payload);
                if (!resp.IsSuccessStatusCode)
                {
                    saveError = await resp.Content.ReadAsStringAsync();
                    return;
                }

                var saved = await resp.Content.ReadFromJsonAsync<TrackedTaskDto>();
                MudDialog.Close(DialogResult.Ok(saved));
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't save correction.");
            }
            finally
            {
                isBusy = false;
            }
        }

        private void Cancel() => MudDialog.Cancel();
    }
}
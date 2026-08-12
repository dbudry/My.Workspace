using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using MudBlazor;
using My.Client.Extensions;
using My.Client.Models;
using My.Client.Services;
using My.Shared.Dtos.StopwatchItem;

namespace My.Client.Components.TrackedTasks
{
    public partial class TrackTaskTime
    {
        private Project? SelectedProject { get; set; }
        private TrackedTask newEntryModel = new();
        private bool isBusy;

        [Parameter]
        public EventCallback<StopwatchItemDto> OnWorkItemStarted { get; set; }

        [Inject] private ISnackbar Snackbar { get; set; } = null!;
        [Inject] private ProjectsCache ProjectsCache { get; set; } = null!;
        [Inject] private StopwatchItemsClient StopwatchItemsClient { get; set; } = null!;

        private async Task AddAndStartEntry()
        {
            if (SelectedProject == null)
            {
                Snackbar.Add("Select a project before starting the timer.", Severity.Warning);
                return;
            }

            isBusy = true;
            try
            {
                var dto = new CreateStopwatchItemDto
                {
                    Name = newEntryModel.Name.Trim(),
                    ProjectId = SelectedProject.ProjectId
                };

                var item = await StopwatchItemsClient.CreateAndStartAsync(dto);

                newEntryModel = new();
                SelectedProject = null;

                Snackbar.Add("Work item started", Severity.Success);
                await OnWorkItemStarted.InvokeAsync(item);
            }
            catch (AccessTokenNotAvailableException ex)
            {
                ex.Redirect();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't start the work item.");
            }
            finally
            {
                isBusy = false;
            }
        }

        /// <summary>
        /// Same as Tasks: active projects via <see cref="ProjectsCache.LookupActiveAsync"/>.
        /// Empty search returns the first page so click-to-open shows a full list.
        /// </summary>
        private async Task<IEnumerable<Project>> SearchProjects(string? value, CancellationToken token)
        {
            try
            {
                return await ProjectsCache.LookupActiveAsync(search: value);
            }
            catch (AccessTokenNotAvailableException ex)
            {
                ex.Redirect();
                return Enumerable.Empty<Project>();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't search projects.");
                return Enumerable.Empty<Project>();
            }
        }

        private void HandleInvalidSubmit(EditContext context)
        {
            foreach (var errorMessage in context.GetValidationMessages())
                Snackbar.Add(errorMessage, Severity.Error);
        }
    }
}

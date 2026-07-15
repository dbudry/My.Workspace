using Microsoft.AspNetCore.Components;
using MudBlazor;
using System.Net.Http.Json;
using My.Client.Extensions;
using My.Client.Models;
using My.Client.Models.Paging;
using My.Client.Services;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Constants;
using My.Shared.Rules;

namespace My.Client.Components.TrackedTasks
{
    public partial class TrackedTaskTable
    {
        public readonly Dictionary<string, List<TrackedTask>> trackedTasksDictionary = new();

        PagingAttributes pagingAttributes = new PagingAttributes();

        private int currentPage = 0;

        private bool isLoading = true;

        #region Dependency Injection

        [Inject]
        private IHttpClientFactory ClientFactory { get; set; } = null!;

        [Inject]
        private ISnackbar Snackbar { get; set; } = null!;

        [Inject]
        private IDialogService DialogService { get; set; } = null!;

        [Inject]
        private UserSettingsService SettingsService { get; set; } = null!;

        [Inject]
        private TrackedTasksClient TrackedTasksClient { get; set; } = null!;

        #endregion

        protected override async Task OnInitializedAsync()
        {
            await Task.WhenAll(LoadTrackedTasks(1, 10), SettingsService.GetSettingsAsync());
        }

        /// <summary>Row color bar resolved per the user's color-source preference.
        /// Null when the preference is None or the project has no source color set.</summary>
        private string? GetRowColor(TrackedTask task)
            => ProjectColorRules.Resolve(
                task.Project?.OrganizationColor,
                task.Project?.ProjectGroupColor,
                SettingsService.ProjectColorSource);

        /// <summary>Tooltip text naming the Org/Group whose color is drawn on the row.</summary>
        private string GetRowLabel(TrackedTask task)
            => ProjectColorRules.ResolveLabel(
                task.Project?.OrganizationName,
                task.Project?.ProjectGroupName,
                task.Project?.OrganizationColor,
                task.Project?.ProjectGroupColor,
                SettingsService.ProjectColorSource) ?? string.Empty;

        private async Task LoadTrackedTasks(int pageNumber, int pageSize)
        {
            var client = ClientFactory.CreateClient(Constants.API.ClientName);

            isLoading = true;
            try
            {
                var query = new ListQueryParameters
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    SortBy = "StartDate",
                    SortDescending = true
                };
                var pagedResponse = await TrackedTasksClient.LoadPageAsync(query);

                if (pagedResponse != null)
                {
                    pagingAttributes.TotalPageCount = pagedResponse.TotalPages;
                    pagingAttributes.PageSize = pagedResponse.PageSize;
                    pagingAttributes.TotalCount = pagedResponse.TotalCount;

                    var trackedTaskList = pagedResponse.Items
                        .Select(dto => new TrackedTask(dto))
                        .ToList();

                    GroupTrackedTasksByDay(trackedTaskList);
                    pagingAttributes.Count = trackedTaskList.Count;
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load tracked tasks.");
            }
            finally
            {
                isLoading = false;
            }

            await InvokeAsync(StateHasChanged);
        }

        private void PageChanged(int page)
        {
            currentPage = page;
            _ = LoadTrackedTasks(page + 1, pagingAttributes.PageSize);
        }

        private void GroupTrackedTasksByDay(List<TrackedTask> trackedTaskList)
        {
            trackedTasksDictionary.Clear();
            var groups = trackedTaskList
                .GroupBy(x => x.StartDate.ToLongDateString())
                .OrderByDescending(groups => DateTime.Parse(groups.Key))
                .ToDictionary(x => x.Key, y => y.ToList());

            foreach (var group in groups)
            {
                trackedTasksDictionary.Add(group.Key, group.Value);
            }
        }

        public async Task RefreshTable()
        {
            await LoadTrackedTasks(1, 10);
        }

        private async Task OpenTaskDialogAsync(TrackedTask task)
        {
            await SettingsService.GetSettingsAsync();
            var client = ClientFactory.CreateClient(Constants.API.ClientName);

            var parameters = new DialogParameters<TrackedTaskDialog>
            {
                { x => x.Mode, task.IsLocked ? TrackedTaskDialogMode.ReadOnly : TrackedTaskDialogMode.Edit },
                { x => x.TaskId, task.TaskId },
                { x => x.TaskName, task.Name },
                { x => x.ProjectId, task.ProjectId },
                { x => x.ProjectName, task.Project?.DisplayName },
                { x => x.StartDate, task.StartDate },
                { x => x.EndDate, task.EndDate },
                { x => x.Duration, task.Duration },
                { x => x.IsAllDay, task.IsAllDay },
                { x => x.Use24HourTime, SettingsService.Use24HourTime },
                { x => x.HttpClient, client }
            };

            var dialog = await DialogService.ShowAsync<TrackedTaskDialog>(task.Name, parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
            var result = await dialog.Result;

            if (result is { Canceled: false })
            {
                await RefreshTable();
            }
        }
    }
}

using Microsoft.AspNetCore.Components;
using MudBlazor;
using My.Client.Extensions;
using My.Client.Helpers;
using My.Client.Models;
using My.Client.Models.Paging;
using My.Client.Services;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.StopwatchItem;
using My.Shared.Rules;

namespace My.Client.Components.TrackedTasks
{
    public partial class StopwatchItemList : IDisposable
    {
        private readonly List<StopwatchItemDto> items = new();
        private PagingAttributes pagingAttributes = new();
        private int currentPage;
        private bool isLoading = true;
        private string? busyItemId;
        private bool tickLoopRunning;
        private CancellationTokenSource? tickCts;

        [Inject] private ISnackbar Snackbar { get; set; } = null!;
        [Inject] private IDialogService DialogService { get; set; } = null!;
        [Inject] private UserSettingsService SettingsService { get; set; } = null!;
        [Inject] private StopwatchItemsClient StopwatchItemsClient { get; set; } = null!;
        [Inject] private StopwatchLocalCache LocalCache { get; set; } = null!;
        [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
        [Inject] private ProjectsCache ProjectsCache { get; set; } = null!;

        protected override async Task OnInitializedAsync()
        {
            var cached = await LocalCache.LoadItemsAsync();
            if (cached is { Count: > 0 })
            {
                items.Clear();
                items.AddRange(cached);
                isLoading = false;
            }

            await SettingsService.GetSettingsAsync();
            _ = LoadItems(currentPage + 1, pagingAttributes.PageSize > 0 ? pagingAttributes.PageSize : 10, showSpinner: items.Count == 0);
            StartTickLoop();
        }

        private string? GetRowColor(StopwatchItemDto item)
            => ProjectColorRules.Resolve(
                item.Project?.OrganizationColor,
                item.Project?.ProjectGroupColor,
                SettingsService.ProjectColorSource);

        private string GetRowLabel(StopwatchItemDto item)
            => ProjectColorRules.ResolveLabel(
                item.Project?.OrganizationName,
                item.Project?.ProjectGroupName,
                item.Project?.OrganizationColor,
                item.Project?.ProjectGroupColor,
                SettingsService.ProjectColorSource) ?? string.Empty;

        private static string? GetProjectDisplayName(StopwatchItemDto item)
            => ProjectDisplayHelper.FromDto(item.Project);

        private string FormatTotal(StopwatchItemDto item)
        {
            var total = item.TotalDuration;
            if (item.IsRunning && item.ActiveSessionStartDate.HasValue)
                total += StopwatchRules.ElapsedForActiveSession(item.ActiveSessionStartDate.Value, null);

            return $"{(int)total.TotalHours:00}:{total.Minutes:00}:{total.Seconds:00}";
        }

        private async Task LoadItems(int pageNumber, int pageSize, bool showSpinner = true)
        {
            if (showSpinner)
                isLoading = true;

            try
            {
                var query = new ListQueryParameters
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    SortBy = "LastWorkedAt",
                    SortDescending = true
                };
                var paged = await StopwatchItemsClient.LoadPageAsync(query);

                items.Clear();
                items.AddRange(paged.Items);
                pagingAttributes.TotalPageCount = paged.TotalPages;
                pagingAttributes.PageSize = paged.PageSize;
                pagingAttributes.TotalCount = paged.TotalCount;
                pagingAttributes.Count = paged.Items.Count();

                await PersistLocalAsync();
            }
            catch (Exception ex)
            {
                if (items.Count == 0)
                    Snackbar.AddApiError(ex, "Couldn't load work items.");
            }
            finally
            {
                isLoading = false;
            }

            await InvokeAsync(StateHasChanged);
        }

        public Task RefreshAsync(bool showSpinner = false)
            => LoadItems(currentPage + 1, pagingAttributes.PageSize > 0 ? pagingAttributes.PageSize : 10, showSpinner);

        public async Task UpsertFromServerAsync(StopwatchItemDto item)
        {
            if (item.IsRunning)
            {
                var now = item.ActiveSessionStartDate ?? DateTime.UtcNow;
                foreach (var other in items.Where(i => i.IsRunning && i.StopwatchItemId != item.StopwatchItemId))
                    ApplyOptimisticStop(other, now);
            }

            ReplaceItem(item);
            if (item.IsRunning)
                MoveToTop(item);

            await PersistLocalAsync();
            await InvokeAsync(StateHasChanged);
        }

        private void PageChanged(int page)
        {
            currentPage = page;
            _ = LoadItems(page + 1, pagingAttributes.PageSize);
        }

        private async Task StartAsync(StopwatchItemDto item)
        {
            if (busyItemId != null) return;
            busyItemId = item.StopwatchItemId;

            var now = DateTime.UtcNow;
            ApplyOptimisticStart(item, now);
            await PersistLocalAsync();
            await InvokeAsync(StateHasChanged);

            try
            {
                var updated = await StopwatchItemsClient.StartAsync(item.StopwatchItemId);
                ReplaceItem(updated);
                await PersistLocalAsync();
            }
            catch (Exception ex)
            {
                await RefreshAsync();
                Snackbar.AddApiError(ex, "Couldn't start the timer.");
            }
            finally
            {
                busyItemId = null;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task StopAsync(StopwatchItemDto item)
        {
            if (busyItemId != null) return;
            busyItemId = item.StopwatchItemId;

            var now = DateTime.UtcNow;
            ApplyOptimisticStop(item, now);
            await PersistLocalAsync();
            await InvokeAsync(StateHasChanged);

            try
            {
                var updated = await StopwatchItemsClient.StopAsync(item.StopwatchItemId);
                ReplaceItem(updated);
                await PersistLocalAsync();
            }
            catch (Exception ex)
            {
                await RefreshAsync();
                Snackbar.AddApiError(ex, "Couldn't stop the timer.");
            }
            finally
            {
                busyItemId = null;
                await InvokeAsync(StateHasChanged);
            }
        }

        private void ApplyOptimisticStart(StopwatchItemDto item, DateTime nowUtc)
        {
            foreach (var other in items.Where(i => i.IsRunning && i.StopwatchItemId != item.StopwatchItemId))
                ApplyOptimisticStop(other, nowUtc);

            item.IsRunning = true;
            item.ActiveSessionStartDate = nowUtc;
            item.LastWorkedAt = nowUtc;
            MoveToTop(item);
        }

        private static void ApplyOptimisticStop(StopwatchItemDto item, DateTime nowUtc)
        {
            if (!item.IsRunning || !item.ActiveSessionStartDate.HasValue)
                return;

            var elapsed = StopwatchRules.RoundUpToMinute(
                StopwatchRules.ElapsedForActiveSession(item.ActiveSessionStartDate.Value, nowUtc));
            item.TotalDuration += elapsed;
            item.IsRunning = false;
            item.ActiveSessionId = null;
            item.ActiveSessionStartDate = null;
            item.LastWorkedAt = nowUtc;
        }

        private void MoveToTop(StopwatchItemDto item)
        {
            items.Remove(item);
            items.Insert(0, item);
        }

        private void ReplaceItem(StopwatchItemDto updated)
        {
            var index = items.FindIndex(i => i.StopwatchItemId == updated.StopwatchItemId);
            if (index >= 0)
                items[index] = updated;
            else
                items.Insert(0, updated);
        }

        private async Task PersistLocalAsync()
        {
            await LocalCache.SaveItemsAsync(items);

            var running = items.FirstOrDefault(i => i.IsRunning);
            await LocalCache.SaveRunningStateAsync(running == null
                ? null
                : new StopwatchRunningState
                {
                    RunningItemId = running.StopwatchItemId,
                    ActiveSessionId = running.ActiveSessionId,
                    SegmentStartedAtUtc = running.ActiveSessionStartDate
                });
        }

        private async Task<IEnumerable<Project>> SearchProjects(string? value, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(value))
                return RecentProjectSuggestions.FromStopwatchItems(items);

            try
            {
                var results = await ProjectsCache.LookupAsync(search: value);
                return results.Where(p => p.IsActive && !p.IsArchived);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't search projects.");
                return Enumerable.Empty<Project>();
            }
        }

        private async Task OpenEditItemAsync(StopwatchItemDto item)
        {
            var parameters = new DialogParameters<StopwatchItemDialog>
            {
                { x => x.ItemId, item.StopwatchItemId },
                { x => x.ItemName, item.Name },
                { x => x.ProjectId, item.ProjectId },
                { x => x.ProjectName, item.Project?.Name ?? GetProjectDisplayName(item) },
                { x => x.SearchProjects, (Func<string?, CancellationToken, Task<IEnumerable<Project>>>)SearchProjects }
            };

            var dialog = await DialogService.ShowAsync<StopwatchItemDialog>(
                "Edit work item",
                parameters,
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });

            var result = await dialog.Result;
            if (result is not { Canceled: false, Data: (string savedName, string savedProjectId) })
                return;

            try
            {
                var updated = await StopwatchItemsClient.UpdateAsync(new UpdateStopwatchItemDto
                {
                    StopwatchItemId = item.StopwatchItemId,
                    Name = savedName,
                    ProjectId = savedProjectId
                });
                ReplaceItem(updated);
                await PersistLocalAsync();
                Snackbar.Add("Work item saved.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't save the work item.");
            }
        }

        private async Task DeleteItemAsync(StopwatchItemDto item)
        {
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Delete work item",
                $"Delete \"{item.Name}\" and all of its logged sessions? This can't be undone.",
                yesText: "Delete",
                cancelText: "Cancel");

            if (confirmed != true)
                return;

            try
            {
                await StopwatchItemsClient.DeleteAsync(item.StopwatchItemId);
                items.Remove(item);
                await PersistLocalAsync();
                Snackbar.Add("Work item deleted.", Severity.Success);
                await InvokeAsync(StateHasChanged);

                // Reload so paging counts stay correct and any next-page item slides into view.
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't delete the work item.");
            }
        }

        private async Task OpenSessionsAsync(StopwatchItemDto item)
        {
            await SettingsService.GetSettingsAsync();
            var client = ClientFactory.CreateClient(My.Shared.Constants.Constants.API.ClientName);

            var parameters = new DialogParameters<StopwatchSessionsDialog>
            {
                { x => x.ItemId, item.StopwatchItemId },
                { x => x.ItemName, item.Name },
                { x => x.ItemProjectName, GetProjectDisplayName(item) },
                { x => x.HttpClient, client }
            };

            var dialog = await DialogService.ShowAsync<StopwatchSessionsDialog>(item.Name, parameters,
                new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
            var result = await dialog.Result;

            if (result is { Canceled: false })
                await RefreshAsync();
        }

        private void StartTickLoop()
        {
            if (tickLoopRunning) return;
            tickLoopRunning = true;
            tickCts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                var token = tickCts.Token;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(1000, token);
                        if (items.Any(i => i.IsRunning))
                            await InvokeAsync(StateHasChanged);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, tickCts.Token);
        }

        public void Dispose()
        {
            tickCts?.Cancel();
            tickCts?.Dispose();
        }
    }
}
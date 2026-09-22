using Microsoft.AspNetCore.Components;
using MudBlazor;
using My.Client.Components.Layout;
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
        private ProjectColorSource projectColorSource = ProjectColorSource.ProjectGroup;
        private bool isSavingColorSource;
        private ListMode listMode = ListMode.Items;
        private DateTime weekStartMonday = WeekEntryGridRules.GetWeekStartMonday(DateTime.Today);
        private readonly List<DayViewSection> daySections = new();
        private readonly HashSet<string> expandedKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<StopwatchDayViewRules.SessionSlice>> itemExpandSessions = new(StringComparer.Ordinal);
        private readonly HashSet<string> loadingExpandKeys = new(StringComparer.Ordinal);
        private bool isDayLoading;

        private enum ListMode { Items, Day }

        private static readonly SegmentedTab<ListMode>[] ListModeTabs =
        [
            new(ListMode.Items, "List"),
            new(ListMode.Day, "Week"),
        ];

        private sealed class DayViewSection
        {
            public required DateTime Day { get; init; }
            public required List<DayViewRow> Rows { get; init; }
        }

        private sealed class DayViewRow
        {
            public required string ExpandKey { get; init; }
            public required StopwatchItemDto Item { get; init; }
            public required IReadOnlyList<StopwatchDayViewRules.SessionSlice> Sessions { get; init; }
            public TimeSpan CompletedDuration { get; init; }
        }

        /// <summary>Mini pop-out: keep a real table at every width (scroll sideways if needed).</summary>
        [Parameter] public bool Compact { get; set; }

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

            var settings = await SettingsService.GetSettingsAsync();
            projectColorSource = NormalizeLabelColorToggle(settings.ProjectColorSource);
            _ = LoadItems(currentPage + 1, pagingAttributes.PageSize > 0 ? pagingAttributes.PageSize : 10, showSpinner: items.Count == 0);
            StartTickLoop();
        }

        private static ProjectColorSource NormalizeLabelColorToggle(ProjectColorSource source) =>
            source == ProjectColorSource.Organization
                ? ProjectColorSource.Organization
                : ProjectColorSource.ProjectGroup;

        private async Task OnProjectColorSourceChangedAsync(ProjectColorSource source)
        {
            source = NormalizeLabelColorToggle(source);
            if (source == projectColorSource) return;

            var previous = projectColorSource;
            projectColorSource = source;
            await InvokeAsync(StateHasChanged);

            isSavingColorSource = true;
            try
            {
                await SettingsService.UpdateProjectColorSourceAsync(source);
            }
            catch (Exception ex)
            {
                projectColorSource = previous;
                Snackbar.AddApiError(ex, "Couldn't save label color preference.");
                await InvokeAsync(StateHasChanged);
            }
            finally
            {
                isSavingColorSource = false;
                await InvokeAsync(StateHasChanged);
            }
        }

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
            if (listMode == ListMode.Day)
                await LoadDayViewAsync();
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
                itemExpandSessions.Remove(item.StopwatchItemId);
                if (listMode == ListMode.Day)
                    await LoadDayViewAsync();
                else if (expandedKeys.Contains(item.StopwatchItemId))
                    await LoadItemExpandSessionsAsync(item.StopwatchItemId);
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
                itemExpandSessions.Remove(item.StopwatchItemId);
                if (listMode == ListMode.Day)
                    await LoadDayViewAsync();
                else if (expandedKeys.Contains(item.StopwatchItemId))
                    await LoadItemExpandSessionsAsync(item.StopwatchItemId);
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

        /// <summary>Same as Tasks: full active-project lookup (not stopwatch recent-only).</summary>
        private async Task<IEnumerable<Project>> SearchProjects(string? value, CancellationToken token)
        {
            try
            {
                return await ProjectsCache.LookupActiveAsync(search: value);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't search projects.");
                return Enumerable.Empty<Project>();
            }
        }

        private async Task OpenEditItemAsync(StopwatchItemDto item)
        {
            if (item.HasLockedSessions)
            {
                Snackbar.Add(
                    "Name and project cannot change while any session is in a submitted month. Open sessions to edit unlocked durations.",
                    Severity.Warning);
                return;
            }

            var parameters = new DialogParameters<StopwatchItemDialog>
            {
                { x => x.ItemId, item.StopwatchItemId },
                { x => x.ItemName, item.Details },
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
                var updated = await StopwatchItemsClient.UpdateAsync(new UpdateStopwatchItemDto {
                    StopwatchItemId = item.StopwatchItemId,
                    Details = savedName,
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

        /// <summary>
        /// Removes the item from this list only. Nothing is deleted — the item and every
        /// session under it are untouched server-side; they're still there in Tasks/Reports.
        /// Actually deleting a work item (and all its sessions) now lives inside the Sessions
        /// dialog — see StopwatchSessionsDialog.DeleteWorkItemAsync — so it's not one accidental
        /// click away from here.
        /// </summary>
        private async Task ClearItemAsync(StopwatchItemDto item)
        {
            if (item.IsRunning)
            {
                Snackbar.Add("Stop the timer before removing this from your list.", Severity.Warning);
                return;
            }

            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Remove from your list",
                $"Remove \"{item.Details}\" from your Work Items list? Its logged time isn't affected — you'll still find it in Tasks and Reports.",
                yesText: "Remove",
                cancelText: "Cancel");

            if (confirmed != true)
                return;

            if (busyItemId != null)
                return;

            busyItemId = item.StopwatchItemId;
            try
            {
                await StopwatchItemsClient.ClearAsync(item.StopwatchItemId);
                items.Remove(item);
                await PersistLocalAsync();
                Snackbar.Add("Removed from your list.", Severity.Success);
                await InvokeAsync(StateHasChanged);

                // Reload so paging counts stay correct and any next-page item slides into view.
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't remove the work item from your list.");
            }
            finally
            {
                busyItemId = null;
            }
        }

        private async Task OpenSessionsAsync(StopwatchItemDto item)
        {
            await SettingsService.GetSettingsAsync();
            var client = ClientFactory.CreateClient(My.Shared.Constants.Constants.API.ClientName);

            var parameters = new DialogParameters<StopwatchSessionsDialog>
            {
                { x => x.ItemId, item.StopwatchItemId },
                { x => x.ItemName, item.Details },
                { x => x.ItemProjectId, item.ProjectId },
                { x => x.ItemProjectName, GetProjectDisplayName(item) },
                { x => x.HttpClient, client }
            };

            var dialog = await DialogService.ShowAsync<StopwatchSessionsDialog>(item.Details, parameters,
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
                        if (items.Any(i => i.IsRunning)
                            || daySections.SelectMany(d => d.Rows).Any(r => r.Item.IsRunning))
                            await InvokeAsync(StateHasChanged);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, tickCts.Token);
        }

        private async Task SetListModeAsync(ListMode mode)
        {
            if (listMode == mode) return;
            listMode = mode;
            expandedKeys.Clear();
            if (mode == ListMode.Day)
                await LoadDayViewAsync();
        }

        private async Task OnWeekNavChangedAsync(DateTime monday)
        {
            if (monday.Date == weekStartMonday.Date) return;
            weekStartMonday = monday.Date;
            expandedKeys.Clear();
            await LoadDayViewAsync();
        }

        private bool IsCurrentWeek =>
            weekStartMonday.Date == WeekEntryGridRules.GetWeekStartMonday(DateTime.Today).Date;

        private async Task LoadDayViewAsync()
        {
            isDayLoading = true;
            await InvokeAsync(StateHasChanged);
            try
            {
                var tz = SettingsService.GetTimeZoneInfo();
                var fromUtc = DateTimeWire.ToUtc(weekStartMonday.Date, tz);
                var toUtc = DateTimeWire.ToUtc(weekStartMonday.Date.AddDays(7), tz);
                var dto = await StopwatchItemsClient.LoadDayViewAsync(fromUtc, toUtc);
                var itemsById = dto.Items.ToDictionary(i => i.StopwatchItemId, StringComparer.Ordinal);
                var today = DateTime.Today;
                var weekEnd = WeekEntryGridRules.GetWeekEndSunday(weekStartMonday).Date;

                var slices = new List<StopwatchDayViewRules.SessionSlice>();
                foreach (var sessionDto in dto.Sessions)
                {
                    var model = new TrackedTask(sessionDto, tz);
                    var startUtc = DateTime.SpecifyKind(sessionDto.StartDate, DateTimeKind.Utc);
                    var day = model.StartDate.Date;
                    if (model.IsRunning && (day < weekStartMonday.Date || day > weekEnd))
                        day = today;
                    slices.Add(ToSlice(model, day, startUtc));
                }

                var grouped = StopwatchDayViewRules.GroupByDayThenItem(slices);
                daySections.Clear();
                foreach (var section in grouped)
                {
                    daySections.Add(new DayViewSection
                    {
                        Day = section.Day,
                        Rows = section.Items.Select(row => new DayViewRow
                        {
                            ExpandKey = $"{section.Day:yyyy-MM-dd}|{row.StopwatchItemId}",
                            Item = itemsById.GetValueOrDefault(row.StopwatchItemId)
                                ?? new StopwatchItemDto
                                {
                                    StopwatchItemId = row.StopwatchItemId,
                                    Details = "Work item"
                                },
                            Sessions = row.Sessions,
                            CompletedDuration = row.CompletedDuration
                        }).ToList()
                    });
                }

                if (IsCurrentWeek
                    && daySections.Any(s => s.Rows.Count > 0)
                    && daySections.All(s => s.Day.Date != today))
                {
                    daySections.Insert(0, new DayViewSection { Day = today, Rows = [] });
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load the day view.");
            }
            finally
            {
                isDayLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task ToggleExpandAsync(string key, StopwatchItemDto? itemForLazyLoad = null)
        {
            if (!expandedKeys.Add(key))
            {
                expandedKeys.Remove(key);
                return;
            }

            if (itemForLazyLoad == null || itemExpandSessions.ContainsKey(key))
                return;

            await LoadItemExpandSessionsAsync(itemForLazyLoad.StopwatchItemId);
        }

        private async Task LoadItemExpandSessionsAsync(string itemId)
        {
            loadingExpandKeys.Add(itemId);
            await InvokeAsync(StateHasChanged);
            try
            {
                var dtos = await StopwatchItemsClient.LoadSessionsAsync(itemId);
                var tz = SettingsService.GetTimeZoneInfo();
                var slices = new List<StopwatchDayViewRules.SessionSlice>();
                foreach (var sessionDto in dtos.OrderByDescending(d => d.StartDate))
                {
                    var model = new TrackedTask(sessionDto, tz);
                    var startUtc = DateTime.SpecifyKind(sessionDto.StartDate, DateTimeKind.Utc);
                    slices.Add(ToSlice(model, model.StartDate.Date, startUtc));
                }

                itemExpandSessions[itemId] = slices;
            }
            catch (Exception ex)
            {
                expandedKeys.Remove(itemId);
                Snackbar.AddApiError(ex, "Couldn't load sessions.");
            }
            finally
            {
                loadingExpandKeys.Remove(itemId);
                await InvokeAsync(StateHasChanged);
            }
        }

        private static StopwatchDayViewRules.SessionSlice ToSlice(
            TrackedTask model, DateTime day, DateTime startUtc)
            => new()
            {
                TaskId = model.TaskId,
                StopwatchItemId = model.StopwatchItemId ?? "",
                Day = day.Date,
                StartLocal = model.StartDate,
                EndLocal = model.EndDate,
                StartUtc = startUtc,
                Duration = model.IsRunning ? TimeSpan.Zero : model.Duration,
                IsLocked = model.IsLocked,
                IsRunning = model.IsRunning
            };

        private string FormatDayTotal(DayViewRow row)
        {
            var total = row.CompletedDuration;
            if (row.Item.IsRunning && row.Sessions.Any(s => s.IsRunning) && row.Item.ActiveSessionStartDate.HasValue)
                total += StopwatchRules.ElapsedForActiveSession(row.Item.ActiveSessionStartDate.Value, null);
            return $"{(int)total.TotalHours:00}:{total.Minutes:00}:{total.Seconds:00}";
        }

        private static string DayHeading(DateTime day)
        {
            var label = day.ToString("dddd, MMM d");
            return day.Date == DateTime.Today ? $"{label} (Today)" : label;
        }

        private bool CanStart(StopwatchItemDto item) =>
            !item.IsCleared && !string.IsNullOrEmpty(item.ProjectId);

        private IReadOnlyList<StopwatchDayViewRules.SessionSlice> ItemExpandSessions(string itemId) =>
            itemExpandSessions.TryGetValue(itemId, out var slices) ? slices : [];

        public void Dispose()
        {
            tickCts?.Cancel();
            tickCts?.Dispose();
        }
    }
}
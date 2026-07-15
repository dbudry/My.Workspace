using System.Net.Http.Json;
using My.Client.Models;
using My.Shared.Constants;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.TaskList;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Helpers;

namespace My.Client.Services
{
    /// <summary>
    /// Fetches tracked tasks with server-side paging. Use <see cref="LoadRangeAsync"/>
    /// when a screen needs every row in a bounded date window (calendar, reports).
    /// </summary>
    public class TrackedTasksClient
    {
        private readonly IHttpClientFactory _clientFactory;

        // Coalesces identical in-flight range requests keyed by URL. On a fresh sign-in the
        // authorized page initializes twice (the auth state settles in two beats), firing two
        // overlapping identical range loads. Sharing the one in-flight fetch collapses them to a
        // single HTTP call. The entry is removed the moment the fetch completes, so a later reload
        // (e.g. after editing a task) always fetches fresh — this never serves stale data.
        // WASM is single-threaded, so no locking is needed around this dictionary.
        private readonly Dictionary<string, Task<List<TrackedTaskDto>>> _inFlightRange = new();

        public TrackedTasksClient(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        /// <summary>
        /// One page of the unified Tasks list (stopwatch work items + manual entries), merged,
        /// sorted, and paged on the server via GET /tasklist — so the client never pulls the whole
        /// dataset just to render 50 rows.
        /// </summary>
        public async Task<PagedResponse<TaskListRowDto>> LoadTaskListAsync(
            ListQueryParameters query,
            CancellationToken cancellationToken = default)
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var url = ListQueryUrlBuilder.Build(Constants.API.TaskList.Get, query);
            return await client.GetFromJsonAsync<PagedResponse<TaskListRowDto>>(url, cancellationToken)
                ?? new PagedResponse<TaskListRowDto>();
        }

        public async Task<PagedResponse<TrackedTaskDto>> LoadPageAsync(
            ListQueryParameters query,
            DateTime? from = null,
            DateTime? to = null,
            bool excludeStopwatchSessions = false,
            CancellationToken cancellationToken = default)
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var url = TrackedTaskQueryUrlBuilder.Build(
                Constants.API.TrackedTask.Get, query, from, to, excludeStopwatchSessions);
            return await client.GetFromJsonAsync<PagedResponse<TrackedTaskDto>>(url, cancellationToken)
                ?? new PagedResponse<TrackedTaskDto>();
        }

        /// <summary>
        /// All rows in a date window via GET /trackedtasks/range — one HTTP call, one DB round-trip.
        /// </summary>
        public async Task<List<TrackedTask>> LoadRangeAsync(
            DateTime? from,
            DateTime? to,
            string? search = null,
            bool excludeStopwatchSessions = false,
            CancellationToken cancellationToken = default)
        {
            var url = TrackedTaskQueryUrlBuilder.BuildRange(
                Constants.API.TrackedTask.GetRange, from, to, search, excludeStopwatchSessions);

            if (!_inFlightRange.TryGetValue(url, out var fetch))
            {
                fetch = FetchRangeAsync(url);
                _inFlightRange[url] = fetch;

                // Clear the entry once the fetch settles so the next load always hits the API
                // fresh. Registering the removal *after* the add (rather than in a finally inside
                // FetchRangeAsync) avoids an ordering bug: a synchronously-completing fetch would
                // otherwise run its finally before we ever stored it, stranding a stale entry.
                _ = fetch.ContinueWith(
                    _ =>
                    {
                        if (_inFlightRange.TryGetValue(url, out var current) && current == fetch)
                            _inFlightRange.Remove(url);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            // WaitAsync lets a caller honor its own cancellation without cancelling the shared
            // fetch that other callers are still awaiting. Each caller projects its own model
            // instances so no mutable state is shared between them.
            var dtos = await fetch.WaitAsync(cancellationToken);
            return dtos.Select(d => new TrackedTask(d)).ToList();
        }

        private async Task<List<TrackedTaskDto>> FetchRangeAsync(string url)
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            return await client.GetFromJsonAsync<List<TrackedTaskDto>>(url)
                ?? new List<TrackedTaskDto>();
        }
    }
}
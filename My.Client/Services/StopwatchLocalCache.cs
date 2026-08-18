using My.Shared.Dtos.StopwatchItem;

namespace My.Client.Services
{
    /// <summary>
    /// Persists stopwatch work items and running-timer metadata in localStorage so the
    /// stopwatch page renders immediately and keeps ticking when the API is slow or offline.
    /// </summary>
    public class StopwatchLocalCache
    {
        private const string ItemsKey = "stopwatchItemsCache";
        private const string RunningKey = "stopwatchRunningState";

        private readonly LocalStorageService _storage;

        public StopwatchLocalCache(LocalStorageService storage)
        {
            _storage = storage;
        }

        public async Task SaveItemsAsync(IEnumerable<StopwatchItemDto> items)
        {
            try
            {
                await _storage.SetItemAsync(ItemsKey, new CachedItems { Items = items.ToList() });
            }
            catch { /* localStorage unavailable */ }
        }

        public async Task<List<StopwatchItemDto>?> LoadItemsAsync()
        {
            try
            {
                return (await _storage.GetItemAsync<CachedItems>(ItemsKey))?.Items;
            }
            catch
            {
                return null;
            }
        }

        public async Task SaveRunningStateAsync(StopwatchRunningState? state)
        {
            try
            {
                if (state == null)
                    await _storage.RemoveItemAsync(RunningKey);
                else
                    await _storage.SetItemAsync(RunningKey, state);
            }
            catch { /* localStorage unavailable */ }
        }

        public async Task<StopwatchRunningState?> LoadRunningStateAsync()
        {
            try
            {
                return await _storage.GetItemAsync<StopwatchRunningState>(RunningKey);
            }
            catch
            {
                return null;
            }
        }

        private class CachedItems
        {
            public List<StopwatchItemDto> Items { get; set; } = new();
        }
    }

    public class StopwatchRunningState
    {
        public string? RunningItemId { get; set; }
        public string? ActiveSessionId { get; set; }
        public DateTime? SegmentStartedAtUtc { get; set; }
    }
}
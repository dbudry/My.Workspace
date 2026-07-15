using System.Net.Http.Json;
using My.Shared.Constants;
using My.Shared.Dtos;

namespace My.Client.Services
{
    /// <summary>
    /// Session-scoped cache of the workspace AppSettings list. Multiple admin/management
    /// pages read these flags (AllowUserDelete, AllowProjectDelete, etc.) on mount;
    /// without a cache each page issues its own GET /appsettings, which is wasted
    /// bandwidth and Function invocations.
    ///
    /// Lifetime: scoped per circuit (one per user session). The AppSettings page itself
    /// invalidates after a save so the next reader sees fresh values.
    /// </summary>
    public class AppSettingsCache
    {
        private readonly IHttpClientFactory _clientFactory;
        private List<AppSettingDto>? _cached;
        private Task<List<AppSettingDto>>? _inflight;

        public AppSettingsCache(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public event Action? Changed;

        public async Task<IReadOnlyList<AppSettingDto>> GetAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _cached is not null)
                return _cached;

            _inflight ??= LoadAsync();
            try
            {
                _cached = await _inflight;
            }
            finally
            {
                _inflight = null;
            }
            return _cached;
        }

        public void Invalidate()
        {
            _cached = null;
            _inflight = null;
            Changed?.Invoke();
        }

        private async Task<List<AppSettingDto>> LoadAsync()
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var settings = await client.GetFromJsonAsync<List<AppSettingDto>>(Constants.API.AppSettings.Get);
            return settings ?? new List<AppSettingDto>();
        }
    }
}

using System.Text.Json;
using Microsoft.JSInterop;

namespace My.Client.Services
{
    /// <summary>
    /// Lightweight localStorage wrapper via JS interop.
    /// Persists data across sessions, logout/login, and browser restarts
    /// for the same origin (scheme + host + port).
    /// </summary>
    public class LocalStorageService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = null, // keep C# property names as written
            WriteIndented = false
        };

        private readonly IJSRuntime _js;

        public LocalStorageService(IJSRuntime js)
        {
            _js = js;
        }

        public async Task<T?> GetItemAsync<T>(string key)
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", key);
            if (string.IsNullOrEmpty(json))
                return default;

            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }

        public async Task SetItemAsync<T>(string key, T value)
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await _js.InvokeVoidAsync("localStorage.setItem", key, json);
        }

        public async Task RemoveItemAsync(string key)
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", key);
        }
    }
}

using System.Text.Json;
using My.Shared.Constants;

namespace My.Client.Services;

/// <summary>
/// Tracks the role-set the admin has chosen to "view as" so the rest of the
/// app (auth state, API headers, UI gates) can downgrade their effective roles
/// for testing. Persisted in localStorage so the choice survives reloads.
/// Clearing reverts to the user's real DB roles. An empty active list is valid
/// and means "no roles" (useful for testing what an unauthorized user sees).
/// </summary>
public class ImpersonationService
{
    public const string StorageKey = "impersonate-roles";

    private readonly LocalStorageService _storage;
    private List<string>? _roles;
    private bool _initialized;

    public ImpersonationService(LocalStorageService storage)
    {
        _storage = storage;
    }

    public event Action? Changed;

    public IReadOnlyList<string> Roles => _roles ?? (IReadOnlyList<string>)Array.Empty<string>();

    public bool IsActive => _roles is not null;

    public async Task InitAsync()
    {
        if (_initialized) return;
        _initialized = true;
        _roles = await _storage.GetItemAsync<List<string>>(StorageKey);
    }

    public async Task SetAsync(IEnumerable<string>? roles)
    {
        if (roles is null)
        {
            if (_roles is null) return;
            _roles = null;
            await _storage.RemoveItemAsync(StorageKey);
            Changed?.Invoke();
            return;
        }

        var normalized = roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (_roles is not null && _roles.SequenceEqual(normalized, StringComparer.Ordinal)) return;

        _roles = normalized;
        await _storage.SetItemAsync(StorageKey, normalized);
        Changed?.Invoke();
    }

    public Task ClearAsync() => SetAsync(null);
}

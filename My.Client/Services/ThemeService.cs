using My.Client.Theme;

namespace My.Client.Services;

/// <summary>
/// Holds the user's chosen theme mode (Light/Dark/System) and persists it to
/// localStorage. The profile menu and Settings page bind to this
/// so changing the mode in one place updates the other without a refresh.
/// </summary>
public class ThemeService
{
    private readonly LocalStorageService _storage;
    private ThemeMode _mode = ThemeMode.System;
    private bool _initialized;

    public ThemeService(LocalStorageService storage)
    {
        _storage = storage;
    }

    public event Action? Changed;

    public ThemeMode Mode => _mode;

    public async Task InitAsync()
    {
        if (_initialized) return;
        _initialized = true;
        var stored = await _storage.GetItemAsync<string>(AppTheme.ThemeModeStorageKey);
        if (!string.IsNullOrEmpty(stored) && Enum.TryParse<ThemeMode>(stored, ignoreCase: true, out var parsed))
            _mode = parsed;
    }

    public async Task SetAsync(ThemeMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        await _storage.SetItemAsync(AppTheme.ThemeModeStorageKey, mode.ToString());
        Changed?.Invoke();
    }
}

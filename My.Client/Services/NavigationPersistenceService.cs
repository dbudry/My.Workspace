using My.Shared.Navigation;

namespace My.Client.Services;

/// <summary>
/// Persists sidebar accordion expansion and last in-app route to localStorage so a
/// returning user sees their previous nav context. Last-page restore runs only on a
/// new browser session (sessionStorage marker) so clicking Dashboard still works.
/// </summary>
public class NavigationPersistenceService
{
    public const string AccordionStorageKey = "pp-nav-accordion";
    public const string LastPathStorageKey = "pp-nav-last-path";
    public const string SessionActiveKey = "pp-session-active";

    private readonly LocalStorageService _storage;

    private bool _initialized;
    private SidebarNavAccordionSnapshot? _accordionSnapshot;
    private string? _lastPath;

    public NavigationPersistenceService(LocalStorageService storage)
    {
        _storage = storage;
    }

    public async Task InitAsync()
    {
        if (_initialized) return;
        _initialized = true;

        _accordionSnapshot = await _storage.GetItemAsync<SidebarNavAccordionSnapshot>(AccordionStorageKey);

        var storedPath = await _storage.GetItemAsync<string>(LastPathStorageKey);
        _lastPath = string.IsNullOrEmpty(storedPath) ? null : storedPath.Trim().TrimEnd('/');
    }

    public SidebarNavAccordionSnapshot? GetAccordionSnapshot() => _accordionSnapshot;

    public string? GetLastPath() => _lastPath;

    public async Task SaveAccordionAsync(SidebarNavAccordionState state)
    {
        await InitAsync();
        _accordionSnapshot = state.CreateSnapshot();
        await _storage.SetItemAsync(AccordionStorageKey, _accordionSnapshot);
    }

    public async Task SaveLastPathAsync(string relativePath)
    {
        if (!ShouldPersistPath(relativePath))
            return;

        await InitAsync();
        _lastPath = string.IsNullOrEmpty(relativePath) ? null : relativePath;
        await _storage.SetItemAsync(LastPathStorageKey, _lastPath ?? string.Empty);
    }

    public static bool ShouldPersistPath(string relativePath)
    {
        if (relativePath.StartsWith("authentication/", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
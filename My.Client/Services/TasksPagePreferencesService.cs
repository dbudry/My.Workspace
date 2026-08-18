using My.Client.Pages.Tyme;
using My.Shared.Rules;

namespace My.Client.Services;

/// <summary>
/// Remembers Tasks page selections (view mode, project, search, business week) in browser
/// localStorage so they survive reloads and browser restarts on the same origin.
///
/// The selected WEEK is deliberately handled differently — see <see cref="SessionWeeklyWeekStartMonday"/>.
/// </summary>
public class TasksPagePreferencesService
{
    public const string StorageKey = "tyme.tasks.pagePreferences";

    private readonly LocalStorageService _storage;
    private bool _loaded;
    private bool _loadSucceeded;

    public TasksPagePreferences Preferences { get; private set; } = new();

    /// <summary>True after a successful localStorage read (or save).</summary>
    public bool IsLoadSuccessful => _loadSucceeded;

    /// <summary>
    /// In-memory only — deliberately NOT written to localStorage. This service is registered
    /// AddScoped, which in Blazor WebAssembly means one instance for the lifetime of the whole
    /// app (there's only ever one scope), so these survive in-app navigation (Tasks → Dashboard →
    /// Tasks) same as before, but reset to null — and therefore back to the current week — on an
    /// actual page reload, since that tears down the WASM app and creates a fresh scope/instance.
    /// This is what the user asked for: remember the week while clicking around, but a real
    /// reload should land back on today's week rather than wherever you last scrolled to.
    /// </summary>
    public DateTime? SessionWeeklyWeekStartMonday { get; set; }

    /// <summary>Project tab counterpart to <see cref="SessionWeeklyWeekStartMonday"/>.</summary>
    public DateTime? SessionProjectWeekStartMonday { get; set; }

    public TasksPagePreferencesService(LocalStorageService storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// Load from localStorage once per successful read. Retries if a previous attempt failed
    /// (e.g. JS interop not ready on the first call).
    /// </summary>
    public async Task LoadAsync()
    {
        if (_loaded && _loadSucceeded)
            return;

        try
        {
            var stored = await _storage.GetItemAsync<TasksPagePreferences>(StorageKey);
            if (stored != null)
                Preferences = stored;
            _loaded = true;
            _loadSucceeded = true;
        }
        catch
        {
            // Do not mark success — allow a later LoadAsync to retry when JS is ready.
            _loaded = true;
            _loadSucceeded = false;
            // Keep existing Preferences; do not wipe a good in-memory snapshot on a failed read.
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            await _storage.SetItemAsync(StorageKey, Preferences);
            _loadSucceeded = true;
            _loaded = true;
        }
        catch
        {
            // localStorage unavailable (private mode / prerender) — ignore
        }
    }

    public async Task UpdateAsync(Action<TasksPagePreferences> mutate)
    {
        // Prefer a successful load so we don't overwrite stored prefs with a blank object.
        if (!_loadSucceeded)
            await LoadAsync();

        mutate(Preferences);
        await SaveAsync();
    }
}

/// <summary>Serializable snapshot of Tasks page UI state (browser localStorage).</summary>
public class TasksPagePreferences
{
    public string ViewMode { get; set; } = nameof(TasksViewMode.Grid);

    public bool ProjectBusinessWeekOnly { get; set; } = true;

    // The selected week is intentionally NOT stored here — see
    // TasksPagePreferencesService.SessionWeeklyWeekStartMonday / SessionProjectWeekStartMonday.
    // It's in-memory-only so a real page reload lands back on the current week instead of
    // wherever the week nav was last scrolled to, while still surviving in-app navigation.

    public string? SelectedProjectId { get; set; }

    public string? SelectedProjectName { get; set; }

    public string? SearchString { get; set; }

    /// <summary>List (table) or Day (project-style day columns) for Week view.</summary>
    public string WeeklyLayout { get; set; } = "List";

    /// <summary>Business week vs full week for Week → Day layout.</summary>
    public bool WeeklyBusinessWeekOnly { get; set; } = true;

    public static TasksViewMode ParseViewMode(string? raw) =>
        Enum.TryParse<TasksViewMode>(raw, ignoreCase: true, out var mode)
            ? mode
            : TasksViewMode.Grid;

    public static WeeklyLayoutMode ParseWeeklyLayout(string? raw) =>
        Enum.TryParse<WeeklyLayoutMode>(raw, ignoreCase: true, out var mode)
            ? mode
            : WeeklyLayoutMode.List;

    public static DateTime? ParseMonday(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (!DateTime.TryParse(raw, out var d))
            return null;
        return WeekEntryGridRules.GetWeekStartMonday(d);
    }

    public static string FormatMonday(DateTime monday) =>
        monday.Date.ToString("yyyy-MM-dd");
}

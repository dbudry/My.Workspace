using My.Client.Pages.Tyme;
using My.Shared.Rules;

namespace My.Client.Services;

/// <summary>
/// Remembers Tasks page selections (view mode, weeks, project, search, business week)
/// in browser localStorage so they survive reloads and browser restarts on the same origin.
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

    /// <summary>ISO date (yyyy-MM-dd) of the Monday for Project view week.</summary>
    public string? ProjectWeekStartMonday { get; set; }

    /// <summary>ISO date (yyyy-MM-dd) of the Monday for Weekly view week.</summary>
    public string? WeeklyWeekStartMonday { get; set; }

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

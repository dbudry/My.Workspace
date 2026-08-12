using System.Reflection;

namespace My.Client.Services;

/// <summary>
/// Exposes the running app's version and tracks whether today is the first day this
/// browser has seen it, so the sidebar can show a one-day "New" badge next to the
/// version number after a deploy.
///
/// The version itself comes from the assembly's informational version, which the SDK
/// derives from &lt;Version&gt; in My.Client.csproj — the same value CI already reads to
/// tag GitHub releases (see .github/workflows/master.yml, "Read version" step), so
/// there is nothing extra to keep in sync at build time.
/// </summary>
public class AppVersionService
{
    private const string StorageKey = "appVersionSeen";

    private sealed class VersionSeenRecord
    {
        public string Version { get; set; } = string.Empty;

        /// <summary>Local calendar date (yyyy-MM-dd) this version was first seen.</summary>
        public string FirstSeenDate { get; set; } = string.Empty;
    }

    private readonly LocalStorageService _storage;
    private bool _initialized;

    public AppVersionService(LocalStorageService storage)
    {
        _storage = storage;
        CurrentVersion = ResolveCurrentVersion();
    }

    /// <summary>The running app's version string, e.g. "1.2.32".</summary>
    public string CurrentVersion { get; }

    /// <summary>
    /// True only on the calendar day this browser first loaded <see cref="CurrentVersion"/>.
    /// Self-expires the next day without any dismiss action needed.
    /// </summary>
    public bool IsNewVersion { get; private set; }

    public async Task InitAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var record = await _storage.GetItemAsync<VersionSeenRecord>(StorageKey);

        if (record == null || record.Version != CurrentVersion)
        {
            // Either this browser has never recorded a version, or the deployed
            // version changed since the last visit — either way, today is day one
            // for CurrentVersion. Persist immediately so a second tab opened later
            // today still shows the badge, but tomorrow it's gone on its own.
            record = new VersionSeenRecord { Version = CurrentVersion, FirstSeenDate = today };
            await _storage.SetItemAsync(StorageKey, record);
            IsNewVersion = true;
        }
        else
        {
            IsNewVersion = record.FirstSeenDate == today;
        }
    }

    private static string ResolveCurrentVersion()
    {
        var assembly = typeof(AppVersionService).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrEmpty(informational))
        {
            // SourceLink/deterministic builds can append "+<git sha>" — strip it, we
            // only want the human <Version> (e.g. "1.2.32"), not build metadata.
            var plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}

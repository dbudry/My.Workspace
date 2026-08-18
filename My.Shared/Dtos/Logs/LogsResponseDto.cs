namespace My.Shared.Dtos.Logs;

/// <summary>
/// Returned by <c>GET /api/applogs</c>. Wraps the query result plus the
/// observed log-level configuration so the admin page can show "currently set to:
/// Error (via env var X)" without round-tripping again.
/// </summary>
public class LogsResponseDto
{
    public List<LogEntryDto> Entries { get; set; } = new();

    /// <summary>
    /// Diagnostic info read from the Function App's environment so the user knows
    /// what level is currently being captured by App Insights without having to
    /// inspect Azure Portal.
    /// </summary>
    public LogLevelInfoDto LogLevel { get; set; } = new();

    /// <summary>
    /// True when the query ran successfully and Entries is populated. False with a
    /// non-null <see cref="Error"/> when App Insights isn't connected or the query
    /// failed (typically because the managed identity isn't assigned the Monitoring
    /// Reader role on the App Insights resource).
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>User-facing error message when <see cref="Success"/> is false.</summary>
    public string? Error { get; set; }
}

public class LogLevelInfoDto
{
    /// <summary>
    /// Effective minimum log level App Insights is currently capturing. Reflects the
    /// env-var value when set, else "Information" (the level App Insights records when
    /// nothing is configured).
    /// </summary>
    public string EffectiveLevel { get; set; } = "Information";

    /// <summary>Env var name that controls the level — surfaced verbatim for copy-paste into Azure Portal.</summary>
    public string EnvVarName { get; set; } = "AzureFunctionsJobHost__logging__logLevel__default";

    /// <summary>True when the env var is set in the Function App's environment; false when relying on the default.</summary>
    public bool IsSetByEnvVar { get; set; }
}

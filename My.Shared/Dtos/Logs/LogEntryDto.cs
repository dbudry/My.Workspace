namespace My.Shared.Dtos.Logs;

public class LogEntryDto
{
    /// <summary>UTC timestamp when the log line was emitted.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>App Insights severity level (0=Verbose, 1=Info, 2=Warning, 3=Error, 4=Critical).</summary>
    public int SeverityLevel { get; set; }

    /// <summary>"trace" or "exception" — exceptions carry stack info; traces are plain ILogger output.</summary>
    public string Kind { get; set; } = "trace";

    /// <summary>Free-text message. For exceptions, "{Type}: {OuterMessage}".</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Distributed-tracing operation id — useful to correlate a 500 with the surrounding traces.</summary>
    public string? OperationId { get; set; }

    /// <summary>Role/app name. In a multi-app workspace, identifies the function app vs other services.</summary>
    public string? CloudRoleName { get; set; }
}

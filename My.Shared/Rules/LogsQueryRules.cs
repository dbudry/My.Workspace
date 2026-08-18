using System.Globalization;

namespace My.Shared.Rules;

/// <summary>
/// Builds the KQL query the admin Logs page sends to App Insights / Log Analytics.
/// Pulled out as a pure helper so the parameter handling (level clamping, time-range
/// validation, escape behavior) is unit-testable without spinning up the Azure SDK.
///
/// The query targets the <c>traces</c> table (used by ILogger output through the
/// App Insights worker integration). <c>exceptions</c> appear as a separate table —
/// we union them in so a failed call shows both the warning line and the exception
/// stack on the same screen.
/// </summary>
public static class LogsQueryRules
{
    /// <summary>App Insights severity codes used in <c>traces.severityLevel</c>.</summary>
    public enum SeverityLevel
    {
        Verbose = 0,
        Information = 1,
        Warning = 2,
        Error = 3,
        Critical = 4,
    }

    /// <summary>Clamp inputs to sane bounds so a hostile or buggy client can't ask for a
    /// week of logs at Verbose level and choke the workspace.</summary>
    public const int MinHours = 1;
    public const int MaxHours = 168; // 7 days
    public const int MinTop = 1;
    public const int MaxTop = 1000;

    public static int ClampHours(int hours) => Math.Clamp(hours, MinHours, MaxHours);
    public static int ClampTop(int top) => Math.Clamp(top, MinTop, MaxTop);

    public static SeverityLevel ParseLevel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return SeverityLevel.Information;
        return raw.Trim().ToLowerInvariant() switch
        {
            "verbose" or "trace" or "0" => SeverityLevel.Verbose,
            "information" or "info" or "1" => SeverityLevel.Information,
            "warning" or "warn" or "2" => SeverityLevel.Warning,
            "error" or "err" or "3" => SeverityLevel.Error,
            "critical" or "crit" or "fatal" or "4" => SeverityLevel.Critical,
            _ => SeverityLevel.Information
        };
    }

    /// <summary>
    /// KQL that unions <c>traces</c> and <c>exceptions</c>, filters by time + minimum
    /// severity, and projects a flat shape the API endpoint hands to the client.
    /// </summary>
    public static string Build(int hours, SeverityLevel minLevel, int top)
    {
        var clampedHours = ClampHours(hours);
        var clampedTop = ClampTop(top);
        var minSev = (int)minLevel;
        var hoursStr = clampedHours.ToString(CultureInfo.InvariantCulture);
        var minSevStr = minSev.ToString(CultureInfo.InvariantCulture);
        var topStr = clampedTop.ToString(CultureInfo.InvariantCulture);

        // Column alias is `logKind` rather than `kind`: KQL treats `kind` as a
        // reserved word (it's a query modifier — `union kind=outer`, `find kind=`,
        // etc.), so an unqualified `kind` in a project list parses as the modifier
        // and the query 400s with a syntax error at that token.
        return $@"union
    (traces
        | where timestamp > ago({hoursStr}h) and severityLevel >= {minSevStr}
        | extend logKind = 'trace'
        | project timestamp, severityLevel, logKind, message, operation_Id, cloud_RoleName),
    (exceptions
        | where timestamp > ago({hoursStr}h) and severityLevel >= {minSevStr}
        | extend logKind = 'exception'
        | project timestamp,
                  severityLevel,
                  logKind,
                  message = strcat(type, ': ', outerMessage),
                  operation_Id,
                  cloud_RoleName)
| top {topStr} by timestamp desc";
    }
}

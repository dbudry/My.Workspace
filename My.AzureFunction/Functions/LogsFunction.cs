using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using My.Functions.Helpers;
using My.Shared.Constants;
using My.Shared.Dtos.Logs;
using My.Shared.Rules;

namespace My.Functions;

/// <summary>
/// Surfaces App Insights traces and exceptions to the admin Logs page.
///
/// Uses the App Insights Query API (api.applicationinsights.io) keyed by the
/// ApplicationId parsed out of the standard APPLICATIONINSIGHTS_CONNECTION_STRING
/// env var Azure sets automatically when AI is wired up to the Function App.
/// The Function App's managed identity needs the "Monitoring Reader" role on
/// the Application Insights resource — see README.
/// </summary>
public class LogsFunction
{
    private const string ConnectionStringEnvVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    private const string LogLevelEnvVar = "AzureFunctionsJobHost__logging__logLevel__default";
    private const string AppInsightsScope = "https://api.applicationinsights.io/.default";
    private const string AppInsightsBaseUrl = "https://api.applicationinsights.io/v1/apps";

    private readonly HttpClient _http;
    private readonly TokenCredential _credential;
    private readonly string? _appId;
    private readonly ILogger<LogsFunction> _logger;

    public LogsFunction(IHttpClientFactory httpFactory, TokenCredential credential, ILogger<LogsFunction> logger)
    {
        _http = httpFactory.CreateClient();
        _credential = credential;
        _appId = AppInsightsConnectionString.GetApplicationId(
            Environment.GetEnvironmentVariable(ConnectionStringEnvVar));
        _logger = logger;
    }

    [Function("GetLogs")]
    public async Task<IActionResult> GetLogsAsync(
        // Route is a literal: the Functions Worker source generator doesn't reliably
        // inline cross-assembly const strings into route metadata. Single-segment
        // route on purpose — "admin/logs" was deployed cleanly but the running host
        // silently 404'd it. Suspect the host treats the "admin" segment as reserved
        // even when nested under the public /api prefix; "applogs" parallels the
        // existing "appsettings" admin-only endpoint and works.
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "applogs")] HttpRequestData req)
    {
        // Global-Admin-only. Scoped admins (Admin:Tyme) cannot read raw application
        // logs — that's a system-administration capability, not a module concern.
        var principal = new ClaimsPrincipal(req.Identities);
        if (!Constants.Roles.IsGlobalAdmin(principal))
            return new StatusCodeResult(403);

        var hours = int.TryParse(req.Query["hours"], out var h) ? h : 24;
        var top = int.TryParse(req.Query["top"], out var t) ? t : 200;
        var level = LogsQueryRules.ParseLevel(req.Query["level"]);

        var response = new LogsResponseDto
        {
            LogLevel = ReadLogLevelConfig()
        };

        if (string.IsNullOrEmpty(_appId))
        {
            response.Success = false;
            response.Error = "App Insights is not connected to this Function App. In the Azure Portal, open the " +
                             "Function App → Application Insights and turn it on; the " +
                             "APPLICATIONINSIGHTS_CONNECTION_STRING env var will be set automatically.";
            return new OkObjectResult(response);
        }

        var kql = LogsQueryRules.Build(hours, level, top);

        try
        {
            AccessToken token;
            try
            {
                token = await _credential.GetTokenAsync(
                    new TokenRequestContext(new[] { AppInsightsScope }),
                    req.FunctionContext.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Couldn't acquire Azure AD token for App Insights API.");
                response.Success = false;
                response.Error = "Couldn't acquire a managed-identity token. If running locally, sign in with " +
                                 "`az login` or Visual Studio. In Azure, verify the Function App's system-assigned " +
                                 "managed identity is enabled.";
                return new OkObjectResult(response);
            }

            var url = $"{AppInsightsBaseUrl}/{_appId}/query?query={Uri.EscapeDataString(kql)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using var httpResponse = await _http.SendAsync(request, req.FunctionContext.CancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var body = await httpResponse.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "App Insights query failed: {Status} {Body}", (int)httpResponse.StatusCode, body);

                response.Success = false;
                if (httpResponse.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                    httpResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    response.Error = "Permission denied querying App Insights. Assign the Function App's " +
                                     "managed identity the 'Monitoring Reader' role on the Application Insights " +
                                     "resource (Azure Portal → App Insights → Access control (IAM)).";
                }
                else
                {
                    response.Error = $"App Insights query returned HTTP {(int)httpResponse.StatusCode}.";
                }
                return new OkObjectResult(response);
            }

            var json = await httpResponse.Content.ReadAsStringAsync();
            response.Entries = ParseQueryResponse(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query App Insights logs.");
            response.Success = false;
            response.Error = ApiErrorMessages.LogQueryFailed;
        }

        return new OkObjectResult(response);
    }

    /// <summary>
    /// Parses the App Insights Query API JSON shape:
    /// <c>{ tables: [{ columns: [{name, type}, ...], rows: [[...], ...] }] }</c>.
    /// </summary>
    private static List<LogEntryDto> ParseQueryResponse(string json)
    {
        var entries = new List<LogEntryDto>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("tables", out var tables) || tables.GetArrayLength() == 0)
            return entries;

        var table = tables[0];
        if (!table.TryGetProperty("columns", out var columnsEl) ||
            !table.TryGetProperty("rows", out var rowsEl))
            return entries;

        var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        foreach (var col in columnsEl.EnumerateArray())
        {
            if (col.TryGetProperty("name", out var nameEl))
                columnIndex[nameEl.GetString() ?? string.Empty] = i;
            i++;
        }

        foreach (var row in rowsEl.EnumerateArray())
        {
            entries.Add(new LogEntryDto
            {
                Timestamp = ReadDateTime(row, columnIndex, "timestamp"),
                SeverityLevel = ReadInt(row, columnIndex, "severityLevel") ?? 1,
                Kind = ReadString(row, columnIndex, "logKind") ?? "trace",
                Message = ReadString(row, columnIndex, "message") ?? string.Empty,
                OperationId = ReadString(row, columnIndex, "operation_Id"),
                CloudRoleName = ReadString(row, columnIndex, "cloud_RoleName")
            });
        }

        return entries;
    }

    private static string? ReadString(JsonElement row, Dictionary<string, int> idx, string col)
    {
        if (!idx.TryGetValue(col, out var i) || i >= row.GetArrayLength()) return null;
        var el = row[i];
        return el.ValueKind == JsonValueKind.Null ? null : el.GetString();
    }

    private static int? ReadInt(JsonElement row, Dictionary<string, int> idx, string col)
    {
        if (!idx.TryGetValue(col, out var i) || i >= row.GetArrayLength()) return null;
        var el = row[i];
        if (el.ValueKind == JsonValueKind.Number) return el.GetInt32();
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var n)) return n;
        return null;
    }

    private static DateTime ReadDateTime(JsonElement row, Dictionary<string, int> idx, string col)
    {
        if (!idx.TryGetValue(col, out var i) || i >= row.GetArrayLength()) return DateTime.UtcNow;
        var el = row[i];
        if (el.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dt))
        {
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
        return DateTime.UtcNow;
    }

    private static LogLevelInfoDto ReadLogLevelConfig()
    {
        var envValue = Environment.GetEnvironmentVariable(LogLevelEnvVar);
        var isSet = !string.IsNullOrWhiteSpace(envValue);
        return new LogLevelInfoDto
        {
            EffectiveLevel = isSet ? envValue! : "Information",
            EnvVarName = LogLevelEnvVar,
            IsSetByEnvVar = isSet
        };
    }
}

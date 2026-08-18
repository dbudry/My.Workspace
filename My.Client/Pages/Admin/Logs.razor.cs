using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;
using System.Net.Http.Json;
using System.Text;
using My.Client.Extensions;
using My.Shared.Constants;
using My.Shared.Dtos.Logs;

namespace My.Client.Pages.Admin
{
    public partial class Logs
    {
        private bool isLoading = true;
        private bool isGlobalAdmin;
        private LogsResponseDto? response;

        private int selectedHours = 24;
        private int selectedTop = 200;

        [CascadingParameter]
        private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

        [CascadingParameter(Name = "SetPageTitle")]
        private Action<string>? SetPageTitle { get; set; }

        [Inject] private NavigationManager Navigation { get; set; } = null!;
        [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
        [Inject] private ISnackbar Snackbar { get; set; } = null!;
        [Inject] private IJSRuntime JS { get; set; } = null!;

        private HttpClient client = null!;

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;

            if (user.Identity is not { IsAuthenticated: true })
            {
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);
                return;
            }

            isGlobalAdmin = Constants.Roles.IsGlobalAdmin(user);
            SetPageTitle?.Invoke("Logs");

            if (!isGlobalAdmin)
            {
                isLoading = false;
                return;
            }

            client = ClientFactory.CreateClient(Constants.API.ClientName);
            await LoadLogs();
        }

        private async Task LoadLogs()
        {
            isLoading = true;
            StateHasChanged();
            try
            {
                // No min-severity UI: pull all severities that exist in App Insights; the
                // Severity column is the filter. Host write level is Azure env config only.
                var url = Constants.API.Logs.Construct(selectedHours, selectedTop);
                response = await client.GetFromJsonAsync<LogsResponseDto>(url);
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load logs.");
            }
            isLoading = false;
            StateHasChanged();
        }

        private async Task CopyAllAsync()
        {
            if (response?.Entries is not { Count: > 0 } entries) return;

            var sb = new StringBuilder();
            foreach (var e in entries)
                sb.AppendLine(FormatEntry(e));

            await CopyToClipboardAsync(sb.ToString(), $"Copied {entries.Count} log entries to clipboard.");
        }

        private Task CopyEntryAsync(LogEntryDto entry) =>
            CopyToClipboardAsync(FormatEntry(entry), "Log entry copied.");

        private async Task CopyToClipboardAsync(string text, string successMessage)
        {
            try
            {
                await JS.InvokeVoidAsync("navigator.clipboard.writeText", text);
                Snackbar.Add(successMessage, Severity.Success);
            }
            catch (Exception)
            {
                // navigator.clipboard requires a secure context; production is HTTPS so this
                // is just defensive. Fall back to surfacing the text so the user can manually
                // copy out of the snackbar message if the API path ever fails.
                Snackbar.Add("Couldn't write to clipboard. Right-click the row and copy manually.", Severity.Warning);
            }
        }

        private static string FormatEntry(LogEntryDto e)
        {
            var op = string.IsNullOrEmpty(e.OperationId) ? "" : $" (op:{e.OperationId})";
            // App Insights returns UTC; convert for the operator's local clock (same as the table).
            var local = ToLocal(e.Timestamp);
            return $"[{local:yyyy-MM-dd HH:mm:ss}] [{SeverityName(e.SeverityLevel)}] {e.Kind}: {e.Message}{op}";
        }

        /// <summary>
        /// Table cell format — local time, compact month/day for dense log rows.
        /// </summary>
        private static string FormatTimestamp(DateTime utcOrUnspecified) =>
            ToLocal(utcOrUnspecified).ToString("MM/dd HH:mm:ss");

        /// <summary>
        /// App Insights / API timestamps are UTC. JSON often deserializes as
        /// <see cref="DateTimeKind.Unspecified"/>; treat that as UTC so
        /// <see cref="DateTime.ToLocalTime"/> still shifts correctly in the browser.
        /// </summary>
        internal static DateTime ToLocal(DateTime value)
        {
            var utc = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            };
            return utc.ToLocalTime();
        }

        private static string SeverityName(int level) => level switch
        {
            0 => "Verbose",
            1 => "Info",
            2 => "Warn",
            3 => "Error",
            4 => "Crit",
            _ => "?"
        };

        private static Color SeverityColor(int level) => level switch
        {
            0 => Color.Default,
            1 => Color.Info,
            2 => Color.Warning,
            3 => Color.Error,
            4 => Color.Error,
            _ => Color.Default
        };
    }
}

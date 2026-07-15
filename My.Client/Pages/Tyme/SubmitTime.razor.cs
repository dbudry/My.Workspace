using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;
using My.Client.Extensions;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.TimeSubmission;

namespace My.Client.Pages.Tyme
{
    public partial class SubmitTime
    {
        private const string ManagerViewStorageKey = "tyme-submit-manager-view";

        private bool isLoading = true;
        private bool canManage;
        private string? busyKey;
        private List<MonthRow> rows = new();
        private HttpClient client = null!;
        private string _managerView = "my";

        private bool ShowMyMonths => !canManage || _managerView == "my";
        private bool ShowTeamSubmissions => canManage && _managerView == "team";

        private string pageDescription => canManage switch
        {
            false => "Mark a completed month's work as final. Once a month is submitted, its tasks are locked from edits.",
            true when _managerView == "team" =>
                "Review and manage team submissions. Filter by status, employee, or month. Unsubmit a row when an employee needs to correct their time.",
            _ =>
                "Mark your own completed months as final. Switch to Team above to review or unsubmit employee months."
        };

        [CascadingParameter]
        private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

        [Inject]
        private IHttpClientFactory ClientFactory { get; set; } = null!;

        [Inject]
        private ISnackbar Snackbar { get; set; } = null!;

        [Inject]
        private TimeSubmissionEvents SubmissionEvents { get; set; } = null!;

        [Inject]
        private IJSRuntime JS { get; set; } = null!;

        protected override async Task OnInitializedAsync()
        {
            client = ClientFactory.CreateClient(Constants.API.ClientName);

            var authState = await AuthenticationStateTask;
            // Scoped-only: Tyme is a module capability. A pure global Admin (no Tyme:*
            // scope) does not have access — they use impersonation if they need to act
            // as a Tyme user/manager. Matches the Intranet model.
            canManage = Constants.Roles.HasScopedAccess(authState.User, Constants.Scopes.Tyme, Constants.Roles.Manager);

            if (canManage)
                await RestoreManagerViewAsync();

            await LoadAsync();
        }

        private async Task OnManagerViewChangedAsync(string value)
        {
            if (_managerView == value) return;
            _managerView = value;
            try
            {
                await JS.InvokeVoidAsync("sessionStorage.setItem", ManagerViewStorageKey, value);
            }
            catch
            {
                // Non-fatal — view still switches for this session.
            }

            await LoadAsync();
        }

        private async Task RestoreManagerViewAsync()
        {
            try
            {
                var saved = await JS.InvokeAsync<string?>("sessionStorage.getItem", ManagerViewStorageKey);
                if (saved is "my" or "team")
                    _managerView = saved;
                else if (saved == "both")
                    _managerView = "team";
            }
            catch
            {
                // sessionStorage unavailable — keep default.
            }
        }

        private async Task LoadAsync()
        {
            if (canManage && _managerView == "team")
            {
                rows.Clear();
                isLoading = false;
                StateHasChanged();
                return;
            }

            isLoading = true;
            try
            {
                var submissionsTask = client.GetFromJsonAsync<List<TimeSubmissionDto>>(Constants.API.TimeSubmission.Get);
                var overdueTask = client.GetFromJsonAsync<List<OverdueMonthDto>>(Constants.API.TimeSubmission.GetOverdue);

                await Task.WhenAll(submissionsTask, overdueTask);

                var submissions = submissionsTask.Result ?? new List<TimeSubmissionDto>();
                var overdue = overdueTask.Result ?? new List<OverdueMonthDto>();

                var seen = new HashSet<(int Y, int M)>();
                var combined = new List<MonthRow>();

                foreach (var s in submissions)
                {
                    if (seen.Add((s.Year, s.Month)))
                        combined.Add(new MonthRow
                        {
                            Year = s.Year,
                            Month = s.Month,
                            IsSubmitted = true,
                            SubmittedAt = s.SubmittedAt,
                            TimeSubmissionId = s.TimeSubmissionId
                        });
                }
                foreach (var o in overdue)
                {
                    if (seen.Add((o.Year, o.Month)))
                        combined.Add(new MonthRow { Year = o.Year, Month = o.Month, IsSubmitted = false });
                }

                rows = combined
                    .OrderByDescending(r => r.Year)
                    .ThenByDescending(r => r.Month)
                    .ToList();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't load submissions.");
            }
            finally
            {
                isLoading = false;
            }
        }

        private async Task SubmitAsync(MonthRow row)
        {
            busyKey = $"{row.Year}-{row.Month}";
            try
            {
                var resp = await client.PostAsJsonAsync(Constants.API.TimeSubmission.Create,
                    new CreateTimeSubmissionDto { Year = row.Year, Month = row.Month });
                resp.EnsureSuccessStatusCode();
                Snackbar.Add($"Submitted {new DateTime(row.Year, row.Month, 1):MMMM yyyy}.", Severity.Success);
                SubmissionEvents.NotifyChanged();
                await LoadAsync();
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't submit time.");
            }
            finally
            {
                busyKey = null;
            }
        }

        private class MonthRow
        {
            public int Year { get; set; }
            public int Month { get; set; }
            public bool IsSubmitted { get; set; }
            public DateTime? SubmittedAt { get; set; }
            public string? TimeSubmissionId { get; set; }
        }
    }
}

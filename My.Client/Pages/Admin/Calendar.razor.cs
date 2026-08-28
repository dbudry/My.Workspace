using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Net.Http.Json;
using My.Client.Extensions;
using My.Shared.Constants;
using My.Shared.Dtos.GoogleCalendar;
using My.Shared.Dtos.Logs;
using My.Shared.Rules;

namespace My.Client.Pages.Admin;

public partial class Calendar
{
    private bool isLoading = true;
    private bool isProbing;
    private bool isRenewing;
    private bool isGlobalAdmin;
    private GoogleCalendarSyncStatusDto? status;
    private LogsResponseDto? activity;
    private GoogleCalendarQueueProbeDto? probeResult;

    [CascadingParameter]
    private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

    [CascadingParameter(Name = "SetPageTitle")]
    private Action<string>? SetPageTitle { get; set; }

    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IHttpClientFactory ClientFactory { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;

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
        SetPageTitle?.Invoke("Google Calendar");

        if (!isGlobalAdmin)
        {
            isLoading = false;
            return;
        }

        client = ClientFactory.CreateClient(Constants.API.ClientName);
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        isLoading = true;
        StateHasChanged();
        try
        {
            var statusTask = client.GetFromJsonAsync<GoogleCalendarSyncStatusDto>(
                Constants.API.GoogleCalendar.SyncStatus);
            var activityTask = client.GetFromJsonAsync<LogsResponseDto>(
                Constants.API.Logs.Construct(24, 50, GoogleCalendarSyncRules.TopicCalendar));
            await Task.WhenAll(statusTask, activityTask);
            status = statusTask.Result;
            activity = activityTask.Result;
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't load Google Calendar status.");
        }
        isLoading = false;
        StateHasChanged();
    }

    private async Task ProbeQueueAsync()
    {
        isProbing = true;
        probeResult = null;
        StateHasChanged();
        try
        {
            using var response = await client.PostAsync(Constants.API.GoogleCalendar.ProbeQueue, null);
            probeResult = await response.Content.ReadFromJsonAsync<GoogleCalendarQueueProbeDto>();
            if (probeResult?.Success == true)
                Snackbar.Add(probeResult.Message, Severity.Success);
            else
                Snackbar.Add(probeResult?.Message ?? "Queue test failed.", Severity.Error);
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't test the import queue.");
        }
        isProbing = false;
        StateHasChanged();
    }

    private async Task RenewWatchesAsync()
    {
        isRenewing = true;
        StateHasChanged();
        try
        {
            using var response = await client.PostAsync(Constants.API.GoogleCalendar.RenewWatches, null);
            var result = await response.Content.ReadFromJsonAsync<GoogleCalendarWatchRenewalResultDto>();
            if (result?.Success == true)
                Snackbar.Add(result.Message, Severity.Success);
            else
                Snackbar.Add(result?.Message ?? "Couldn't renew watches.", Severity.Error);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't renew watches.");
        }
        isRenewing = false;
        StateHasChanged();
    }

    private async Task PullEventsAsync(GoogleCalendarConnectedUserDto row)
    {
        var parameters = new DialogParameters<PullFromGoogleDialog>
        {
            { x => x.TargetEmail, row.Email }
        };
        var dialog = await DialogService.ShowAsync<PullFromGoogleDialog>(
            "Pull missed events from Google", parameters);
        var dialogResult = await dialog.Result;
        if (dialogResult == null || dialogResult.Canceled
            || dialogResult.Data is not PullFromGoogleDialog.PullRange range)
            return;

        try
        {
            var url = Constants.API.GoogleCalendar.ConstructPullFromGoogle(range.From, range.To, row.UserId);
            using var response = await client.PostAsync(url, null);
            if (!response.IsSuccessStatusCode)
            {
                Snackbar.AddApiError(new HttpRequestException(response.ReasonPhrase), "Couldn't pull Google events.");
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<CalendarPullResultDto>();
            if (!string.IsNullOrWhiteSpace(result?.Error))
                Snackbar.Add(result.Error, Severity.Error);
            else if (result != null)
                Snackbar.Add(CalendarPullResultRules.AdminSnackbar(result), Severity.Success);
            else
                Snackbar.Add($"Pulled Google events for {row.Email}.", Severity.Success);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.AddApiError(ex, "Couldn't pull Google events.");
        }
    }

    private static Color StatusColor(string status) => status switch
    {
        GoogleCalendarSyncRules.StatusReady => Color.Success,
        GoogleCalendarSyncRules.StatusImportOff => Color.Warning,
        GoogleCalendarSyncRules.StatusWatchExpired => Color.Error,
        GoogleCalendarSyncRules.StatusNoWatch => Color.Warning,
        _ => Color.Default
    };

    private static string FormatWhen(DateTime timestamp) =>
        Logs.ToLocal(timestamp).ToString("MM/dd HH:mm:ss");

    private static string LevelName(int level) => level switch
    {
        0 => "Verbose",
        1 => "Info",
        2 => "Warn",
        3 => "Error",
        4 => "Crit",
        _ => "?"
    };

    private static Color LevelColor(int level) => level switch
    {
        2 => Color.Warning,
        3 or 4 => Color.Error,
        1 => Color.Info,
        _ => Color.Default
    };
}

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using My.Client.Components.TrackedTasks;
using My.Shared.Dtos.StopwatchItem;

namespace My.Client.Pages.Tyme
{
    public partial class TaskStopwatch
    {
        private StopwatchItemList stopwatchItemListComponent = null!;

        [CascadingParameter]
        private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

        [CascadingParameter(Name = "SetPageTitle")]
        private Action<string>? SetPageTitle { get; set; }

        [Inject] protected NavigationManager Navigation { get; set; } = null!;
        [Inject] private IJSRuntime JS { get; set; } = null!;

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;

            if (user.Identity != null && !user.Identity.IsAuthenticated)
                Navigation.NavigateTo($"{Navigation.BaseUri}auth/login", true);

            SetPageTitle?.Invoke("Task Timer");
        }

        private async Task HandleWorkItemStarted(StopwatchItemDto item)
        {
            await stopwatchItemListComponent.UpsertFromServerAsync(item);
        }

        private async Task OpenMiniStopwatch()
        {
            var url = Navigation.ToAbsoluteUri("/tyme/stopwatch-mini").ToString();
            // Work Items uses Breakpoint.Xs (stack only below 600px). MudBlazor 9's Sm
            // stacks below 960px, so a 760px pop-out was always the card layout.
            await JS.InvokeVoidAsync("open", url, "_blank",
                "width=760,height=520,menubar=no,toolbar=no,location=no,status=no,resizable=yes");
        }
    }
}

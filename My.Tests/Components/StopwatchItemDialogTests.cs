using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using My.Client.Components.Projects;
using My.Client.Components.TrackedTasks;
using My.Client.Models;
using Xunit;

namespace My.Tests.Components
{
    /// <summary>
    /// End-to-end guard for the Stopwatch "Assign project" / "Edit work item" dialog. This dialog
    /// hosts <see cref="ProjectAutocomplete"/>; a bad parameter on that child (the Dense crash)
    /// took the whole dialog — and the Blazor circuit — down at render time. Mounting it here
    /// through the real MudDialogProvider fails the test instead of the browser.
    /// </summary>
    public class StopwatchItemDialogTests : BunitContext, IAsyncLifetime
    {
        public StopwatchItemDialogTests()
        {
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            JSInterop.Mode = JSRuntimeMode.Loose;
        }

        private static Task<IEnumerable<Project>> NoProjects(string? _, CancellationToken __)
            => Task.FromResult(Enumerable.Empty<Project>());

        [Fact]
        public async Task Mounts_and_hosts_the_project_picker()
        {
            var provider = Render<MudDialogProvider>();
            var dialogService = Services.GetRequiredService<IDialogService>();

            var parameters = new DialogParameters<StopwatchItemDialog>
            {
                { x => x.ItemId, "item-1" },
                { x => x.ItemName, "Test work item" },
                { x => x.SearchProjects, (Func<string?, CancellationToken, Task<IEnumerable<Project>>>)NoProjects }
            };

            await provider.InvokeAsync(() =>
                dialogService.ShowAsync<StopwatchItemDialog>("Edit work item", parameters));

            // If the dialog mounted, its ProjectAutocomplete child is in the tree — the exact
            // thing that failed to render when it was handed an undeclared Dense parameter.
            var picker = provider.FindComponent<ProjectAutocomplete>();
            Assert.False(picker.Instance.Dense); // the dialog requests the roomier, non-dense field
        }

        Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

        async Task IAsyncLifetime.DisposeAsync()
        {
            if (Services is IAsyncDisposable asyncProvider)
                await asyncProvider.DisposeAsync();
        }
    }
}

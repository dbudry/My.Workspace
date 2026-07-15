using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using My.Client.Components.Projects;
using My.Client.Models;
using Xunit;

namespace My.Tests.Components
{
    /// <summary>
    /// Guards against the class of bug where a razor markup passes a parameter the target
    /// component does not declare — the C# compiler can't catch it, so it throws at render
    /// time in the browser ("... does not have a property matching the name 'Dense'").
    /// A dialog once passed Dense="false" to <see cref="ProjectAutocomplete"/> before it
    /// declared that parameter, crashing the Stopwatch "Assign project" / "Edit work item" flow.
    /// </summary>
    public class ProjectAutocompleteTests : BunitContext, IAsyncLifetime
    {
        public ProjectAutocompleteTests()
        {
            // CheckForPopoverProvider=false lets MudAutocomplete's popover initialize without a
            // MudPopoverProvider in the (headless) test render tree.
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            JSInterop.Mode = JSRuntimeMode.Loose;
        }

        private static Task<IEnumerable<Project>> NoProjects(string? _, CancellationToken __)
            => Task.FromResult(Enumerable.Empty<Project>());

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Renders_and_binds_Dense_in_either_density(bool dense)
        {
            var cut = Render<ProjectAutocomplete>(ps => ps
                .Add(p => p.SearchFunc, NoProjects)
                .Add(p => p.Dense, dense));

            // The inner MudAutocomplete must receive the density we asked for. This is the exact
            // binding that used to throw when Dense wasn't a real parameter on ProjectAutocomplete.
            var autocomplete = cut.FindComponent<MudAutocomplete<Project>>();
            Assert.Equal(dense, autocomplete.Instance.Dense);
        }

        [Fact]
        public void Dense_is_a_public_component_parameter()
        {
            var prop = typeof(ProjectAutocomplete).GetProperty("Dense");
            Assert.NotNull(prop);
            Assert.Equal(typeof(bool), prop!.PropertyType);
            Assert.NotNull(prop.GetCustomAttribute<ParameterAttribute>());
        }

        Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

        // MudBlazor registers services that only implement IAsyncDisposable; bUnit's synchronous
        // Dispose() would throw on them. Dispose the provider asynchronously first so the later
        // synchronous Dispose() is a no-op.
        async Task IAsyncLifetime.DisposeAsync()
        {
            if (Services is IAsyncDisposable asyncProvider)
                await asyncProvider.DisposeAsync();
        }
    }
}

using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using My.Client.Components.Projects;
using My.Client.Models;
using My.Client.Services;
using Xunit;

namespace My.Tests.Components
{
    /// <summary>
    /// Guards against the class of bug where a razor markup passes a parameter the target
    /// component does not declare — the C# compiler can't catch it, so it throws at render
    /// time in the browser ("... does not have a property matching the name 'Dense'").
    /// A dialog once passed Dense="false" to <see cref="ProjectAutocomplete"/> before it
    /// declared that parameter, crashing the Stopwatch "Assign project" / "Edit work item" flow.
    ///
    /// ProjectAutocomplete is a MudTextField + results list (not MudAutocomplete). Keep the
    /// smoke test minimal — deep FindComponent walks of MudBlazor trees have crashed the
    /// test host with stack overflow under CI concurrency.
    /// </summary>
    public class ProjectAutocompleteTests : BunitContext, IAsyncLifetime
    {
        public ProjectAutocompleteTests()
        {
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            // Paging uses ProjectsCache + HTTP; empty stub is enough for a render smoke test.
            Services.AddSingleton<IHttpClientFactory>(new EmptyProjectsHttpClientFactory());
            Services.AddSingleton<ProjectsCache>();
            JSInterop.Mode = JSRuntimeMode.Loose;
        }

        private static Task<IEnumerable<Project>> NoProjects(string? _, CancellationToken __)
            => Task.FromResult(Enumerable.Empty<Project>());

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Renders_and_binds_Dense_in_either_density(bool dense)
        {
            // Must not throw when Dense is passed (the original markup-parameter bug).
            var cut = Render<ProjectAutocomplete>(ps => ps
                .Add(p => p.SearchFunc, NoProjects)
                .Add(p => p.Dense, dense));

            Assert.Equal(dense, cut.Instance.Dense);
            // Text-field picker surface is present (avoid FindComponent tree walks that can SO).
            Assert.Contains("mud-input", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mud-autocomplete", cut.Markup, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Returns empty paged project JSON so LookupActivePageAsync does not throw.</summary>
        private sealed class EmptyProjectsHttpClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name)
                => new HttpClient(new EmptyProjectsHandler())
                {
                    BaseAddress = new Uri("https://test.local/")
                };
        }

        private sealed class EmptyProjectsHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = """{"items":[],"pageNumber":1,"pageSize":100,"totalCount":0,"hasNext":false}""";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }
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

    /// <summary>
    /// Own DI graph so the throwing HttpClient is the one <see cref="ProjectsCache"/>
    /// actually receives (a second AddSingleton in the smoke-test class would not
    /// replace the empty stub).
    /// </summary>
    public class ProjectAutocompleteLoadFailureTests : BunitContext, IAsyncLifetime
    {
        public ProjectAutocompleteLoadFailureTests()
        {
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            Services.AddSingleton<IHttpClientFactory>(new ThrowingProjectsHttpClientFactory());
            Services.AddSingleton<ProjectsCache>();
            JSInterop.Mode = JSRuntimeMode.Loose;
        }

        [Fact]
        public async Task Focus_when_projects_api_throws_does_not_crash_the_picker()
        {
            var cut = Render<ProjectAutocomplete>();

            // MudPopover content is portaled (not in cut.Markup). Success is that
            // OnFocusIn completes instead of throwing out to the renderer.
            var exception = await Record.ExceptionAsync(() =>
                cut.Find(".project-autocomplete").TriggerEventAsync("onfocusin", new FocusEventArgs()));

            Assert.Null(exception);
            Assert.Contains("mud-input", cut.Markup, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ThrowingProjectsHttpClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name)
                => new HttpClient(new ThrowingProjectsHandler())
                {
                    BaseAddress = new Uri("https://test.local/")
                };
        }

        private sealed class ThrowingProjectsHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new HttpRequestException("Functions host not ready");
        }

        Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

        async Task IAsyncLifetime.DisposeAsync()
        {
            if (Services is IAsyncDisposable asyncProvider)
                await asyncProvider.DisposeAsync();
        }
    }
}

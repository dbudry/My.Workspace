using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using My.Client.Components.TrackedTasks;
using My.Client.Services;
using Xunit;

namespace My.Tests.Components;

/// <summary>
/// Edit Duration (stopwatch session) hides the project picker and inherits the work
/// item's project. A 25-row name lookup used to miss, leave selectedProject null, and
/// block save with "A project is required to log time." even when ProjectId was set.
/// </summary>
public class TrackedTaskDialogTests : BunitContext, IAsyncLifetime
{
    public TrackedTaskDialogTests()
    {
        Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task Edit_duration_saves_with_work_item_project_id_when_lookup_misses()
    {
        var handler = new RecordingHandler();
        var http = CreateClient(handler);
        RegisterCaches(http);

        var provider = Render<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();

        await provider.InvokeAsync(() =>
            dialogService.ShowAsync<TrackedTaskDialog>("Edit Duration", DurationParameters(
                projectId: "proj-1",
                http: http)));

        provider.WaitForAssertion(() =>
            Assert.Contains("Hours", provider.Markup, StringComparison.Ordinal));

        ClickSave(provider);

        provider.WaitForAssertion(() =>
            Assert.True(handler.PutTrackedTaskCalled, "Save should PUT the session using the work-item ProjectId."));
        Assert.DoesNotContain("A project is required to log time.", provider.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_duration_still_requires_a_project_when_work_item_has_none()
    {
        var handler = new RecordingHandler();
        var http = CreateClient(handler);
        RegisterCaches(http);

        var provider = Render<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();

        await provider.InvokeAsync(() =>
            dialogService.ShowAsync<TrackedTaskDialog>("Edit Duration", DurationParameters(
                projectId: null,
                http: http)));

        provider.WaitForAssertion(() =>
            Assert.Contains("Hours", provider.Markup, StringComparison.Ordinal));

        ClickSave(provider);

        provider.WaitForAssertion(() =>
            Assert.Contains("A project is required to log time.", provider.Markup, StringComparison.Ordinal));
        Assert.False(handler.PutTrackedTaskCalled);
    }

    private void RegisterCaches(HttpClient http)
    {
        Services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(http));
        Services.AddSingleton<ProjectsCache>();
        Services.AddSingleton<AppSettingsCache>();
        Services.AddSingleton<UserSettingsService>();
    }

    private static HttpClient CreateClient(RecordingHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://test.local/") };

    private static DialogParameters<TrackedTaskDialog> DurationParameters(string? projectId, HttpClient http) => new()
    {
        { x => x.Mode, TrackedTaskDialogMode.Edit },
        { x => x.TaskId, "task-1" },
        { x => x.TaskName, "Sample Parameter Trim" },
        { x => x.ProjectId, projectId },
        { x => x.ProjectName, "Sample Project - Parameter Trim" },
        { x => x.StartDate, new DateTime(2026, 8, 18, 9, 22, 0) },
        { x => x.Duration, new TimeSpan(5, 7, 0) },
        { x => x.HttpClient, http },
        { x => x.StopwatchItemId, "item-1" }
    };

    private static void ClickSave(IRenderedComponent<MudDialogProvider> provider)
    {
        var save = provider.FindAll("button")
            .First(b => b.TextContent.Contains("Save", StringComparison.OrdinalIgnoreCase));
        save.Click();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public StubHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    /// <summary>
    /// Lookup/get-by-id miss (the production failure), empty settings, PUT succeeds.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public bool PutTrackedTaskCalled { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";

            if (request.Method == HttpMethod.Put && path.Contains("trackedtasks", StringComparison.OrdinalIgnoreCase))
            {
                PutTrackedTaskCalled = true;
                return Task.FromResult(Ok("{}"));
            }

            if (path.Contains("usersettings", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Ok("{}"));

            if (path.Contains("appsettings", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Ok("[]"));

            if (path.Contains("projectlookup", StringComparison.OrdinalIgnoreCase)
                || path.Contains("projects", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Method == HttpMethod.Get && path.Contains("/projects/", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

                return Task.FromResult(Ok(
                    """{"items":[],"pageNumber":1,"pageSize":25,"totalCount":0,"hasNext":false}"""));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Ok(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (Services is IAsyncDisposable asyncProvider)
            await asyncProvider.DisposeAsync();
    }
}

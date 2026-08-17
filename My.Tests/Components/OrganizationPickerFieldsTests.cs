using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using My.Client.Components.Projects;
using My.Client.Services;
using Xunit;

namespace My.Tests.Components;

/// <summary>
/// Smoke test: the project-dialog org picker must be the custom text+popover
/// control (same as ProjectAutocomplete), not MudSelect or MudAutocomplete.
/// </summary>
public class OrganizationPickerFieldsTests : BunitContext, IAsyncLifetime
{
    public OrganizationPickerFieldsTests()
    {
        Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
        Services.AddSingleton<IHttpClientFactory>(new EmptyOrgsHttpClientFactory());
        Services.AddScoped<OrganizationsCache>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Renders_custom_typeahead_not_mud_autocomplete()
    {
        var cut = Render<OrganizationPickerFields>(ps => ps
            .Add(p => p.OrganizationLabel, "Organization"));

        Assert.Contains("org-picker-field", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mud-autocomplete", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Organization", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (Services is IAsyncDisposable asyncProvider)
            await asyncProvider.DisposeAsync();
    }

    private sealed class EmptyOrgsHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new EmptyOrgsHandler()) { BaseAddress = new Uri("https://test.local/") };
    }

    private sealed class EmptyOrgsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = """{"items":[],"pageNumber":1,"pageSize":25,"totalCount":0,"hasNext":false}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}

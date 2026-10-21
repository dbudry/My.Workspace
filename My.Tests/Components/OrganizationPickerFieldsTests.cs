using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
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

    [Fact]
    public void Disabled_field_stays_custom_typeahead()
    {
        var cut = Render<OrganizationPickerFields>(ps => ps
            .Add(p => p.OrganizationLabel, "Charge to")
            .Add(p => p.Disabled, true)
            .Add(p => p.ShowDepartment, false)
            .Add(p => p.Placeholder, "Type to search…"));

        Assert.Contains("org-picker-field", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mud-autocomplete", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disabled", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Charge to", cut.Markup, StringComparison.OrdinalIgnoreCase);

        cut.Find(".org-picker-field").Click();
        Assert.DoesNotContain("org-picker-open", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Click_in_the_field_opens_the_list_without_a_new_focus()
    {
        var cut = Render<OrganizationPickerFields>(ps => ps
            .Add(p => p.OrganizationLabel, "Charge to")
            .Add(p => p.ShowDepartment, false));

        Assert.DoesNotContain("org-picker-open", cut.Markup, StringComparison.OrdinalIgnoreCase);

        // A press in the input does not raise focusin when the input already has focus.
        cut.Find(".org-picker-field input").TriggerEvent("onmousedown", new MouseEventArgs());

        Assert.Contains("org-picker-open", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Press_in_the_input_opens_the_list_again_after_blur_closes_it()
    {
        var cut = Render<OrganizationPickerFields>(ps => ps
            .Add(p => p.OrganizationLabel, "Charge to")
            .Add(p => p.ShowDepartment, false));

        cut.Find(".org-picker-field input").TriggerEvent("onmousedown", new MouseEventArgs());
        Assert.Contains("org-picker-open", cut.Markup, StringComparison.OrdinalIgnoreCase);

        cut.Find(".org-picker-field input").TriggerEvent("onblur", new FocusEventArgs());
        cut.WaitForAssertion(() =>
            Assert.DoesNotContain("org-picker-open", cut.Markup, StringComparison.OrdinalIgnoreCase));

        // Mud blur can invoke OnBlur twice. Let the second one finish before pressing again.
        await Task.Delay(400);

        cut.Find(".org-picker-field").Click();
        Assert.Contains("org-picker-open", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clear_keeps_the_list_open()
    {
        var cut = Render<OrganizationPickerFields>(ps => ps
            .Add(p => p.OrganizationId, "org-1")
            .Add(p => p.OrganizationName, "Organization Alpha")
            .Add(p => p.ShowDepartment, false)
            .Add(p => p.OrganizationLabel, "Charge to"));

        cut.WaitForAssertion(() => Assert.Contains("Organization Alpha", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("org-picker-open", cut.Markup, StringComparison.OrdinalIgnoreCase);

        cut.Find("button.mud-input-clear-button").Click();

        // Clear goes through the text-change debounce (200ms) before the list is kept open.
        cut.WaitForAssertion(() =>
            Assert.Contains("org-picker-open", cut.Markup, StringComparison.OrdinalIgnoreCase));
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
            // Get-by-id must miss so the picker keeps the name passed in. A 200 paging
            // body would deserialize as an empty organization and hide that name.
            if (request.RequestUri?.AbsolutePath.Contains("/organizations/", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var body = """{"items":[],"pageNumber":1,"pageSize":25,"totalCount":0,"hasNext":false}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}

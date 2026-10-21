using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using My.Client.Components.Expenses;
using My.Shared.Dtos.Expenses;
using Xunit;

namespace My.Tests.Components;

/// <summary>
/// Razor treats word@word as an email address, so "Open PDF@suffix" was shown literally.
/// </summary>
public class ExpensePdfMenuTests : BunitContext, IAsyncLifetime
{
    public ExpensePdfMenuTests()
    {
        Services.AddMudServices();
        Services.AddSingleton<IHttpClientFactory>(new UnusedHttpClientFactory());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Single_report_menu_says_open_pdf_and_download()
    {
        var cut = RenderMenu();

        cut.Find("button").Click();

        Assert.Contains("Open PDF", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(">Download<", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("@suffix", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Open PDF —", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Split_menu_names_each_charge_to()
    {
        var targets = new List<ExpensePdfTarget>
        {
            new() { Name = "Organization Alpha", ChargeToOrganizationId = "a" },
            new() { Name = "Organization Beta", ChargeToOrganizationId = "b" }
        };
        var cut = RenderMenu(targets);

        cut.Find("button").Click();

        Assert.Contains("Open PDF — Organization Alpha", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Download — Organization Alpha", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Open PDF — Organization Beta", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Download — Organization Beta", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("@suffix", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    private IRenderedComponent<IComponent> RenderMenu(IReadOnlyList<ExpensePdfTarget>? targets = null) =>
        Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<ExpensePdfMenu>(1);
            builder.AddAttribute(2, "ReportId", "report-1");
            if (targets != null)
                builder.AddAttribute(3, "Targets", targets);
            builder.CloseComponent();
        });

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (Services is IAsyncDisposable asyncProvider)
            await asyncProvider.DisposeAsync();
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}

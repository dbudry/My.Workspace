using Bunit;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Services;
using My.Client.Components.Common;
using Xunit;

namespace My.Tests.Components;

public class MonthPickerTests : BunitContext, IAsyncLifetime
{
    public MonthPickerTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Field_shows_month_and_year()
    {
        var cut = RenderPicker(new DateTime(2026, 10, 1), _ => { });

        Assert.Equal("Oct 2026", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void Field_is_empty_without_a_value()
    {
        var cut = RenderPicker(null, _ => { });

        Assert.Equal("", cut.Find("input").GetAttribute("value") ?? "");
    }

    // Opening the popup is not tested here: bUnit's HTML renderer overflows the stack on an open
    // MudPopover. The month grid is verified by hand in the running app.

    private IRenderedComponent<IComponent> RenderPicker(DateTime? value, Action<DateTime?> onChanged) =>
        Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MonthPicker>(1);
            builder.AddAttribute(2, "Value", value);
            builder.AddAttribute(3, "Label", "From month");
            builder.AddAttribute(4, "ValueChanged",
                EventCallback.Factory.Create<DateTime?>(this, onChanged));
            builder.CloseComponent();
        });

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (Services is IAsyncDisposable asyncProvider)
            await asyncProvider.DisposeAsync();
    }
}

using Bunit;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Services;
using My.Client.Components.Common;
using Xunit;

namespace My.Tests.Components;

public class MultiSelectCheckListTests : BunitContext, IAsyncLifetime
{
    private static readonly IReadOnlyList<(string Key, string Text)> Options =
        [("id-1", "Alice Adams"), ("id-2", "Bob Brown")];

    public MultiSelectCheckListTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Field_shows_the_name_not_the_id_of_the_picked_employee()
    {
        var cut = RenderList(["id-2"]);

        var input = cut.Find("input");
        Assert.Equal("Bob Brown", input.GetAttribute("value"));
        Assert.DoesNotContain("id-2", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Field_shows_the_all_label_when_nothing_is_picked()
    {
        var cut = RenderList(null);

        Assert.Equal("All employees", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void Field_says_none_when_every_box_is_cleared()
    {
        var cut = RenderList([]);

        Assert.Equal("None selected", cut.Find("input").GetAttribute("value"));
    }

    private IRenderedComponent<IComponent> RenderList(IReadOnlyCollection<string>? selected) =>
        Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MultiSelectCheckList>(1);
            builder.AddAttribute(2, "Options", Options);
            builder.AddAttribute(3, "SelectedKeys", selected);
            builder.AddAttribute(4, "Label", "Employee");
            builder.AddAttribute(5, "AllLabel", "All employees");
            builder.CloseComponent();
        });

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (Services is IAsyncDisposable asyncProvider)
            await asyncProvider.DisposeAsync();
    }
}

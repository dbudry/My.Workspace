using Bunit;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace My.Tests.Components;

/// <summary>
/// The Tyme settings person search passes these attributes through MudAutocomplete.UserAttributes.
/// This proves they reach the real input, which is what Bitwarden looks at.
/// </summary>
public class PasswordManagerAttributesTests : BunitContext, IAsyncLifetime
{
    public PasswordManagerAttributesTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Autocomplete_user_attributes_land_on_the_input()
    {
        var attrs = new Dictionary<string, object>
        {
            ["autocomplete"] = "off",
            ["data-bwignore"] = "true"
        };

        var cut = Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudAutocomplete<string>>(1);
            builder.AddAttribute(2, "Label", "Add a person");
            builder.AddAttribute(3, "UserAttributes", attrs);
            builder.CloseComponent();
        });

        var input = cut.Find("input");
        Assert.Equal("off", input.GetAttribute("autocomplete"));
        Assert.Equal("true", input.GetAttribute("data-bwignore"));
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (Services is IAsyncDisposable asyncProvider)
            await asyncProvider.DisposeAsync();
    }
}

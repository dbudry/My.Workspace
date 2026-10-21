using Bunit;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Services;
using My.Client.Components.Expenses;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Components;

public class ExpenseStatusChipTests : BunitContext, IAsyncLifetime
{
    public ExpenseStatusChipTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Paid_chip_shows_the_date_on_hover()
    {
        var cut = RenderChip(ExpenseStatusRules.Reimbursed, new DateTime(2026, 10, 19));

        var tooltip = cut.FindComponent<MudTooltip>();
        Assert.Equal("10/19/2026", tooltip.Instance.Text);
        Assert.Contains("Paid", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Reimbursed", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Submitted_chip_has_no_payment_date()
    {
        var cut = RenderChip(ExpenseStatusRules.Submitted, new DateTime(2026, 10, 19));

        Assert.DoesNotContain("10/19/2026", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindComponents<MudTooltip>());
        Assert.Contains("Submitted", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Paid_chip_without_a_date_has_no_tooltip()
    {
        var cut = RenderChip(ExpenseStatusRules.Reimbursed, paidOn: null);

        Assert.Empty(cut.FindComponents<MudTooltip>());
        Assert.Contains("Paid", cut.Markup, StringComparison.Ordinal);
    }

    private IRenderedComponent<IComponent> RenderChip(string status, DateTime? paidOn) =>
        Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<ExpenseStatusChip>(1);
            builder.AddAttribute(2, "Status", status);
            builder.AddAttribute(3, "PaymentDate", paidOn);
            builder.CloseComponent();
        });

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (Services is IAsyncDisposable asyncProvider)
            await asyncProvider.DisposeAsync();
    }
}

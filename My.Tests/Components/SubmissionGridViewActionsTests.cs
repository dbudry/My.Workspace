using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using My.Client.Components.TimeSubmissions;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos;
using My.Shared.Dtos.TimeSubmission;
using Xunit;

namespace My.Tests.Components;

/// <summary>
/// The Grid stays a clean color view. Only Submitted cells (unsubmit) and, when the app setting
/// allows it, Not submitted cells (submit for employee) are clickable. Nothing is added to cells.
/// </summary>
public class SubmissionGridViewActionsTests : BunitContext, IAsyncLifetime
{
    private string _onBehalfSetting = "true";
    private string _reminderSetting = "false";
    private bool _noUnsubmittedRow;

    public SubmissionGridViewActionsTests()
    {
        Services.AddMudServices();
        Services.AddSingleton<IHttpClientFactory>(new FakeFactory(() => _onBehalfSetting, () => _reminderSetting, () => _noUnsubmittedRow));
        Services.AddScoped<TimeSubmissionEvents>();
        Services.AddScoped<AppSettingsCache>();
        Services.AddScoped<SubmissionActionsService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Submitted_cell_is_clickable()
    {
        var cut = RenderGrid();

        Assert.Single(cut.FindAll("td.sub-grid-cell--submitted.sub-grid-cell--action"));
    }

    [Fact]
    public void Not_submitted_cell_is_clickable_when_submit_on_behalf_is_on()
    {
        _onBehalfSetting = "true";

        var cut = RenderGrid();

        Assert.Single(cut.FindAll("td.sub-grid-cell--unsubmitted.sub-grid-cell--action"));
    }

    [Fact]
    public void Not_submitted_cell_is_plain_when_submit_on_behalf_is_off()
    {
        _onBehalfSetting = "false";

        var cut = RenderGrid();

        Assert.Empty(cut.FindAll("td.sub-grid-cell--unsubmitted.sub-grid-cell--action"));
        Assert.Single(cut.FindAll("td.sub-grid-cell--unsubmitted"));
    }

    [Fact]
    public void In_progress_and_no_time_cells_are_never_clickable()
    {
        var cut = RenderGrid();

        Assert.Empty(cut.FindAll("td.sub-grid-cell--open.sub-grid-cell--action"));
        Assert.Empty(cut.FindAll("td.sub-grid-cell--nodata.sub-grid-cell--action"));
    }

    [Fact]
    public void Cells_show_only_the_status_text()
    {
        var cut = RenderGrid();

        foreach (var td in cut.FindAll("td[class*='sub-grid-cell--']"))
        {
            Assert.Empty(td.QuerySelectorAll("svg, .mud-icon-root, button"));
        }
    }

    [Fact]
    public void Reminder_off_leaves_in_progress_and_no_time_cells_plain()
    {
        _reminderSetting = "false";

        var cut = RenderGrid();

        Assert.Empty(cut.FindAll("td.sub-grid-cell--open.sub-grid-cell--action"));
    }

    [Fact]
    public void Reminder_on_only_the_not_submitted_cell_offers_it()
    {
        _reminderSetting = "true";
        _onBehalfSetting = "false";

        var cut = RenderGrid();

        // The Submitted cell (Unsubmit) and the Not submitted cell (Send reminder). In progress and No time stay plain.
        Assert.Single(cut.FindAll("td.sub-grid-cell--unsubmitted.sub-grid-cell--action"));
        Assert.Empty(cut.FindAll("td.sub-grid-cell--open.sub-grid-cell--action"));
        Assert.Empty(cut.FindAll("td.sub-grid-cell--nodata.sub-grid-cell--action"));
        Assert.Equal(2, cut.FindAll("td.sub-grid-cell--action").Count);
    }

    [Fact]
    public void Reminder_on_a_row_without_a_not_submitted_cell_stays_plain()
    {
        _reminderSetting = "true";
        _onBehalfSetting = "false";
        _noUnsubmittedRow = true;

        var cut = RenderGrid();

        Assert.Single(cut.FindAll("td.sub-grid-cell--action")); // only the Submitted cell (Unsubmit)
    }

    [Fact]
    public void Reminder_off_and_submit_for_employee_off_leaves_the_not_submitted_cell_plain()
    {
        _reminderSetting = "false";
        _onBehalfSetting = "false";

        var cut = RenderGrid();

        Assert.Empty(cut.FindAll("td.sub-grid-cell--unsubmitted.sub-grid-cell--action"));
    }

    [Fact]
    public void Menu_items_are_alphabetical_with_send_reminder_first()
    {
        _reminderSetting = "true";
        var cut = RenderGrid();

        cut.Find("td.sub-grid-cell--unsubmitted .sub-grid-cell-fill").Click();

        var items = cut.FindAll(".mud-menu-item").Select(i => i.TextContent.Trim()).ToList();
        Assert.Equal(["Send reminder", "Submit for employee"], items);
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (Services is IAsyncDisposable asyncProvider)
            await asyncProvider.DisposeAsync();
    }

    private IRenderedComponent<IComponent> RenderGrid() =>
        Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<SubmissionGridView>(1);
            builder.CloseComponent();
        });

    private sealed class FakeFactory(Func<string> onBehalfSetting, Func<string> reminderSetting, Func<bool> noUnsubmittedRow) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new Handler(onBehalfSetting, reminderSetting, noUnsubmittedRow)) { BaseAddress = new Uri("http://localhost/") };
    }

    private sealed class Handler(Func<string> onBehalfSetting, Func<string> reminderSetting, Func<bool> noUnsubmittedRow) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            object body = Array.Empty<string>();
            if (path.StartsWith(Constants.API.TimeSubmission.GetGrid.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
            {
                body = Grid(noUnsubmittedRow());
            }
            else if (path.StartsWith(Constants.API.AppSettings.Get.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
            {
                body = new[]
                {
                    new AppSettingDto { Key = Constants.SettingKeys.TymeAllowManagerSubmitOnBehalf, Value = onBehalfSetting() },
                    new AppSettingDto { Key = Constants.SettingKeys.TymeUnsubmittedReminderEnabled, Value = reminderSetting() }
                };
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            });
        }

        private static SubmissionGridDto Grid(bool noUnsubmittedRow) => new()
        {
            Months =
            [
                new() { Year = 2026, Month = 6 },
                new() { Year = 2026, Month = 7 },
                new() { Year = 2026, Month = 8 },
                new() { Year = 2026, Month = 9 }
            ],
            Rows =
            [
                new()
                {
                    UserId = "u1", UserName = "Alice Adams",
                    Cells =
                    [
                        new() { Status = SubmissionCellStatus.Submitted, TimeSubmissionId = "s1", SubmittedAt = DateTime.UtcNow },
                        new() { Status = noUnsubmittedRow ? SubmissionCellStatus.Open : SubmissionCellStatus.Unsubmitted },
                        new() { Status = SubmissionCellStatus.Open },
                        new() { Status = SubmissionCellStatus.NoData }
                    ]
                }
            ]
        };
    }
}

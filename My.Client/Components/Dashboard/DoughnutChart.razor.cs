using Microsoft.AspNetCore.Components;
using My.Client.Models.Dashboard;

namespace My.Client.Components.Dashboard
{
    public partial class DoughnutChart
    {
        [Parameter]
        public List<ProjectDataItem> Data { get; set; } = new();

        /// <summary>Optional per-segment palette. Null/empty falls through to MudBlazor's
        /// defaults so callers that don't have per-axis colors (Department pivot, or the
        /// user has opted out of project colors via Settings) don't have to fabricate
        /// placeholders to avoid the chart breaking.</summary>
        [Parameter]
        public string[]? Palette { get; set; }

        /// <summary>Percent (default) shows each segment's share of the whole; Duration
        /// shows the actual time. Also changes what the total line under the chart reads.</summary>
        [Parameter]
        public ChartValueMode ValueMode { get; set; } = ChartValueMode.Percent;
    }
}

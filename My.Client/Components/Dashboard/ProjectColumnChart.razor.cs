using Microsoft.AspNetCore.Components;
using My.Client.Models.Dashboard;

namespace My.Client.Components.Dashboard
{
    public partial class ProjectColumnChart
    {
        [Parameter]
        public List<WorkTimePerMonth> Data { get; set; } = new();
    }
}

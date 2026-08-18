namespace My.Shared.Dtos.Dashboard
{
    public class DashboardDto
    {
        public AmountOfWorkTimeDto ThisMonth { get; set; } = null!;
        public AmountOfWorkTimeDto LastMonth { get; set; } = null!;
        public List<WorkTimePerMonthDto> MonthlyChart { get; set; } = new();
        public List<ProjectDataItemDto> ProjectChart { get; set; } = new();
    }
}

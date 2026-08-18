namespace My.Client.Models.Dashboard
{
    public class ProjectDataItem
    {
        public string ProjectId { get; set; }
        public string ProjectName { get; set; }
        public TimeSpan Time { get; set; }
        public string YearAndMonth { get; set; }
        public double TimeInt { get => Time.TotalSeconds; }

        // Parent IDs/names so the dashboard chart can re-pivot by Organization or
        // Project Group on the client without another round trip.
        public string? OrganizationId { get; set; }
        public string? OrganizationName { get; set; }
        public string? OrganizationColor { get; set; }
        public string? ProjectGroupId { get; set; }
        public string? ProjectGroupName { get; set; }
        public string? ProjectGroupColor { get; set; }

        public ProjectDataItem(string projectId, string projectName, TimeSpan time, string yearAndMonth)
        {
            ProjectId = projectId;
            ProjectName = projectName;
            Time = time;
            YearAndMonth = yearAndMonth;
        }
    }
}

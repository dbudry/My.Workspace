namespace My.Shared.Dtos.Project
{
    /// <summary>
    /// Whether a project can be permanently deleted and what history would be orphaned.
    /// </summary>
    public class ProjectDeleteImpactDto
    {
        public int TaskCount { get; set; }
        public int GoogleCalendarTaskCount { get; set; }
        public int TeamCalendarTaskCount { get; set; }
        public int ManagerAliasCount { get; set; }
        public bool CanDelete { get; set; }
        public string? BlockReason { get; set; }
    }
}
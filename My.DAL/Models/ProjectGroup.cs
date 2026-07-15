namespace My.DAL.Models
{
    /// <summary>
    /// Workspace-shared visual grouping for projects. Just a label and color — calendar
    /// routing happens via the project's own slug, not the group's.
    /// </summary>
    public class ProjectGroup
    {
        public string ProjectGroupId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Color { get; set; } = "#616161";
        public ICollection<Project>? Projects { get; set; }
    }
}

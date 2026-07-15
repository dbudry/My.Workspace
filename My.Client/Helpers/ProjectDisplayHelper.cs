using My.Client.Models;
using My.Shared.Dtos.Project;

namespace My.Client.Helpers
{
    public static class ProjectDisplayHelper
    {
        public static string? FromDto(ProjectDto? project)
        {
            if (project == null) return null;
            return string.IsNullOrEmpty(project.ProjectGroupName)
                ? project.Name
                : $"{project.ProjectGroupName} - {project.Name}";
        }

        public static string? FromModel(Project? project)
        {
            if (project == null) return null;
            return project.DisplayName;
        }
    }
}
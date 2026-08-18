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

        /// <summary>
        /// Secondary label under a project name: organization and/or project group,
        /// joined with a middle dot. Null when neither is set.
        /// </summary>
        public static string? AffiliationCaption(string? organizationName, string? projectGroupName)
        {
            var org = string.IsNullOrWhiteSpace(organizationName) ? null : organizationName.Trim();
            var group = string.IsNullOrWhiteSpace(projectGroupName) ? null : projectGroupName.Trim();

            if (org == null && group == null) return null;
            if (org == null) return group;
            if (group == null) return org;
            return $"{org} · {group}";
        }
    }
}
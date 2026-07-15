using My.Client.Models;
using My.Shared.Dtos.StopwatchItem;

namespace My.Client.Helpers
{
    /// <summary>
    /// Derives the project-picker suggestions shown before the user types a search term.
    /// </summary>
    public static class RecentProjectSuggestions
    {
        public const int MaxCount = 5;

        public static IReadOnlyList<Project> FromStopwatchItems(
            IEnumerable<StopwatchItemDto> items,
            int maxCount = MaxCount)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var results = new List<Project>();

            foreach (var item in items
                         .Where(i => !string.IsNullOrEmpty(i.ProjectId) && i.Project != null)
                         .OrderByDescending(i => i.LastWorkedAt))
            {
                if (!seen.Add(item.ProjectId!))
                    continue;

                var project = new Project(item.Project!);
                if (!project.IsActive || project.IsArchived)
                    continue;

                results.Add(project);
                if (results.Count >= maxCount)
                    break;
            }

            return results;
        }
    }
}
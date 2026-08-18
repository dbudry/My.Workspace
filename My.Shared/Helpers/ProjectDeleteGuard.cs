using My.Shared.Dtos.Project;

namespace My.Shared.Helpers
{
    /// <summary>
    /// Rules for permanently deleting a project (including availability categories).
    /// Logged time or calendar links block delete; archive/inactive is the safe path.
    /// </summary>
    public static class ProjectDeleteGuard
    {
        public static ProjectDeleteImpactDto Evaluate(
            int taskCount,
            int googleCalendarTaskCount,
            int teamCalendarTaskCount,
            int managerAliasCount)
        {
            var impact = new ProjectDeleteImpactDto
            {
                TaskCount = taskCount,
                GoogleCalendarTaskCount = googleCalendarTaskCount,
                TeamCalendarTaskCount = teamCalendarTaskCount,
                ManagerAliasCount = managerAliasCount,
                CanDelete = taskCount == 0 && managerAliasCount == 0
            };

            if (!impact.CanDelete)
                impact.BlockReason = BuildBlockReason(impact);

            return impact;
        }

        public static string BuildBlockReason(ProjectDeleteImpactDto impact, string entityLabel = "project")
        {
            var parts = new List<string>();

            if (impact.TaskCount > 0)
            {
                parts.Add($"{impact.TaskCount:N0} logged time entr{(impact.TaskCount == 1 ? "y" : "ies")}");
            }

            if (impact.ManagerAliasCount > 0)
            {
                parts.Add($"{impact.ManagerAliasCount:N0} manager override{(impact.ManagerAliasCount == 1 ? "" : "s")}");
            }

            var calendarParts = new List<string>();
            if (impact.GoogleCalendarTaskCount > 0)
                calendarParts.Add($"{impact.GoogleCalendarTaskCount:N0} on personal Google Calendar");
            if (impact.TeamCalendarTaskCount > 0)
                calendarParts.Add($"{impact.TeamCalendarTaskCount:N0} on Team Availability calendar");

            var history = parts.Count > 0
                ? string.Join(" and ", parts)
                : "linked history";

            var message =
                $"This {entityLabel} has {history}. Deleting would orphan that data while Google Calendar events could remain disconnected.";

            if (calendarParts.Count > 0)
                message += $" Includes {string.Join(" and ", calendarParts)}.";

            message += " Archive or set inactive instead.";
            return message;
        }
    }
}
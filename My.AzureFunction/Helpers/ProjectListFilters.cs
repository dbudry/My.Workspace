using System.Linq.Expressions;
using My.DAL.Models;

namespace My.Functions.Helpers
{
    internal static class ProjectListFilters
    {
        internal static Expression<Func<Project, bool>> Build(
            bool includeArchived,
            bool includeInactive,
            string? search,
            bool sharedAvailabilityOnly = false)
        {
            var term = search?.Trim();
            if (string.IsNullOrEmpty(term))
            {
                return p =>
                    ((!p.IsArchived && (p.IsActive || includeInactive))
                        || (p.IsArchived && includeArchived))
                    && (!sharedAvailabilityOnly || p.IsSharedAvailability);
            }

            return p =>
                ((!p.IsArchived && (p.IsActive || includeInactive))
                    || (p.IsArchived && includeArchived))
                && (!sharedAvailabilityOnly || p.IsSharedAvailability)
                && (p.Name.Contains(term)
                    || (p.DisplayName != null && p.DisplayName.Contains(term))
                    || (p.Slug != null && p.Slug.Contains(term))
                    || (p.Organization != null && p.Organization.Name.Contains(term))
                    || (p.Department != null && p.Department.Name.Contains(term))
                    || (p.ProjectGroup != null && p.ProjectGroup.Name.Contains(term)));
        }

        internal static Func<IQueryable<Project>, IOrderedQueryable<Project>> OrderBy(
            string? sortBy,
            bool sortDescending)
        {
            return (sortBy ?? "Name").ToLowerInvariant() switch
            {
                "organization" or "organizationname" => sortDescending
                    ? q => q.OrderByDescending(p => p.Organization!.Name)
                    : q => q.OrderBy(p => p.Organization!.Name),
                "group" or "projectgroup" or "projectgroupname" => sortDescending
                    ? q => q.OrderByDescending(p => p.ProjectGroup!.Name)
                    : q => q.OrderBy(p => p.ProjectGroup!.Name),
                "slug" => sortDescending
                    ? q => q.OrderByDescending(p => p.Slug)
                    : q => q.OrderBy(p => p.Slug),
                _ => sortDescending
                    ? q => q.OrderByDescending(p => p.Name)
                    : q => q.OrderBy(p => p.Name)
            };
        }

        /// <summary>
        /// Grouped manager list: parent (organization or project group) first, then the
        /// user's selected project sort within each parent.
        /// </summary>
        internal static Func<IQueryable<Project>, IOrderedQueryable<Project>> OrderByGrouped(
            bool byOrganization,
            string? sortBy,
            bool sortDescending)
        {
            return q =>
            {
                var ordered = byOrganization
                    ? q.OrderBy(p => p.OrganizationId == null)
                        .ThenBy(p => p.Organization!.Name)
                    : q.OrderBy(p => p.ProjectGroupId == null)
                        .ThenBy(p => p.ProjectGroup!.Name);

                return ApplyGroupedSecondarySort(ordered, byOrganization, sortBy, sortDescending);
            };
        }

        private static IOrderedQueryable<Project> ApplyGroupedSecondarySort(
            IOrderedQueryable<Project> ordered,
            bool byOrganization,
            string? sortBy,
            bool sortDescending)
        {
            return (sortBy ?? "Name").ToLowerInvariant() switch
            {
                "organization" or "organizationname" when !byOrganization => sortDescending
                    ? ordered.ThenByDescending(p => p.Organization!.Name).ThenByDescending(p => p.Name)
                    : ordered.ThenBy(p => p.Organization!.Name).ThenBy(p => p.Name),
                "group" or "projectgroup" or "projectgroupname" when byOrganization => sortDescending
                    ? ordered.ThenByDescending(p => p.ProjectGroup!.Name).ThenByDescending(p => p.Name)
                    : ordered.ThenBy(p => p.ProjectGroup!.Name).ThenBy(p => p.Name),
                "slug" => sortDescending
                    ? ordered.ThenByDescending(p => p.Slug)
                    : ordered.ThenBy(p => p.Slug),
                "name" => sortDescending
                    ? ordered.ThenByDescending(p => p.Name)
                    : ordered.ThenBy(p => p.Name),
                _ => sortDescending
                    ? ordered.ThenByDescending(p => p.Name)
                    : ordered.ThenBy(p => p.Name)
            };
        }
    }
}
using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Models.Paging;
using My.Shared.Constants;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.Project;

namespace My.Functions.Helpers
{
    /// <summary>
    /// Builds the manager projects table server-side: pages by project rows (not org/group
    /// buckets), emits a parent header when the organization or project group changes, and
    /// repeats that header at the start of the next page when a parent spans pages.
    /// </summary>
    internal static class ProjectGroupedListQuery
    {
        private const string NullParentKey = "\0";

        internal static async Task<PagedResponse<ProjectListRowDto>> QueryAsync(
            ApplicationDbContext db,
            PagingParameters parameters,
            bool includeArchived,
            bool includeInactive,
            string groupBy,
            string? search,
            bool sharedAvailabilityOnly = false)
        {
            var filter = ProjectListFilters.Build(includeArchived, includeInactive, search, sharedAvailabilityOnly);
            var baseQuery = db.Projects.AsNoTracking().Where(filter);

            if (string.Equals(groupBy, ProjectListGroupBy.Organization, StringComparison.OrdinalIgnoreCase))
                return await QueryPagedByParentAsync(db, baseQuery, parameters, byOrganization: true, search);

            if (string.Equals(groupBy, ProjectListGroupBy.ProjectGroup, StringComparison.OrdinalIgnoreCase))
                return await QueryPagedByParentAsync(db, baseQuery, parameters, byOrganization: false, search);

            throw new ArgumentException($"Unsupported groupBy value: {groupBy}", nameof(groupBy));
        }

        private static async Task<PagedResponse<ProjectListRowDto>> QueryPagedByParentAsync(
            ApplicationDbContext db,
            IQueryable<Project> baseQuery,
            PagingParameters parameters,
            bool byOrganization,
            string? search)
        {
            var ordered = ProjectListFilters.OrderByGrouped(byOrganization, parameters.SortBy, parameters.SortDescending)(baseQuery);

            var totalCount = await ordered.CountAsync();
            var pageNumber = parameters.PageNumber < 1 ? 1 : parameters.PageNumber;
            var pageSize = parameters.PageSize < 1 ? PagingParameters.DefaultPageSize : parameters.PageSize;

            var pageProjects = pageSize == 0
                ? new List<ProjectDto>()
                : await ProjectDtos(ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize)).ToListAsync();

            var parentCounts = await LoadParentCountsAsync(baseQuery, byOrganization);
            var rows = BuildRows(pageProjects, byOrganization, parentCounts);

            // Empty project groups never appear when headers are derived only from projects
            // on the page — so managers couldn't open Edit/Delete on a newly created group.
            // On ProjectGroup mode, inject headers for groups with zero matching projects.
            if (!byOrganization)
                await InsertEmptyProjectGroupHeadersAsync(db, rows, parentCounts, search);

            var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            // No projects but empty groups (or headers only): still expose one page of rows.
            if (totalCount == 0 && rows.Count > 0)
                totalPages = 1;

            return new PagedResponse<ProjectListRowDto>
            {
                Items = rows,
                // MudTable hides Items when TotalCount is 0 — use row count as a floor.
                TotalCount = totalCount == 0 && rows.Count > 0 ? rows.Count : totalCount,
                PageSize = pageSize,
                CurrentPage = pageNumber,
                TotalPages = totalPages,
                HasNext = pageNumber < totalPages,
                HasPrevious = pageNumber > 1
            };
        }

        private static async Task InsertEmptyProjectGroupHeadersAsync(
            ApplicationDbContext db,
            List<ProjectListRowDto> rows,
            Dictionary<string, int> parentCounts,
            string? search)
        {
            var term = search?.Trim();
            var groupsQuery = db.ProjectGroups.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(term))
                groupsQuery = groupsQuery.Where(g => g.Name.Contains(term));

            var allGroups = await groupsQuery
                .OrderBy(g => g.Name)
                .Select(g => new { g.ProjectGroupId, g.Name, g.Color })
                .ToListAsync();

            var presentIds = rows
                .Where(r => r.Kind == ProjectListRowKind.ProjectGroup && r.ProjectGroup != null)
                .Select(r => r.ProjectGroup!.ProjectGroupId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var emptyHeaders = new List<ProjectListRowDto>();
            foreach (var g in allGroups)
            {
                if (presentIds.Contains(g.ProjectGroupId))
                    continue;
                if (parentCounts.TryGetValue(g.ProjectGroupId, out var c) && c > 0)
                    continue; // has projects on another page — don't show empty header here

                emptyHeaders.Add(new ProjectListRowDto
                {
                    Kind = ProjectListRowKind.ProjectGroup,
                    ProjectGroup = new ProjectListGroupHeaderDto
                    {
                        ProjectGroupId = g.ProjectGroupId,
                        Name = g.Name,
                        Color = g.Color,
                        ProjectCount = 0
                    }
                });
            }

            if (emptyHeaders.Count == 0)
                return;

            var noGroupIndex = rows.FindIndex(r => r.Kind == ProjectListRowKind.UnassignedBucket);
            if (noGroupIndex >= 0)
                rows.InsertRange(noGroupIndex, emptyHeaders);
            else
                rows.AddRange(emptyHeaders);
        }

        private static async Task<Dictionary<string, int>> LoadParentCountsAsync(
            IQueryable<Project> baseQuery,
            bool byOrganization)
        {
            if (byOrganization)
            {
                var counts = await baseQuery
                    .GroupBy(p => p.OrganizationId)
                    .Select(g => new { Id = g.Key, Count = g.Count() })
                    .ToListAsync();
                return counts.ToDictionary(x => x.Id ?? NullParentKey, x => x.Count);
            }

            var groupCounts = await baseQuery
                .GroupBy(p => p.ProjectGroupId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToListAsync();
            return groupCounts.ToDictionary(x => x.Id ?? NullParentKey, x => x.Count);
        }

        private static List<ProjectListRowDto> BuildRows(
            IReadOnlyList<ProjectDto> pageProjects,
            bool byOrganization,
            Dictionary<string, int> parentCounts)
        {
            var rows = new List<ProjectListRowDto>();
            string? lastParentKey = null;

            foreach (var project in pageProjects)
            {
                var parentKey = byOrganization ? project.OrganizationId : project.ProjectGroupId;
                if (!string.Equals(parentKey, lastParentKey, StringComparison.Ordinal))
                {
                    var countKey = parentKey ?? NullParentKey;
                    rows.Add(BuildHeader(project, byOrganization, parentCounts.GetValueOrDefault(countKey)));
                    lastParentKey = parentKey;
                }

                rows.Add(ProjectRow(project));
            }

            return rows;
        }

        private static ProjectListRowDto BuildHeader(
            ProjectDto project,
            bool byOrganization,
            int projectCount)
        {
            if (byOrganization)
            {
                if (project.OrganizationId == null)
                {
                    return new ProjectListRowDto
                    {
                        Kind = ProjectListRowKind.UnassignedBucket,
                        BucketLabel = "No Organization",
                        ProjectCount = projectCount
                    };
                }

                return new ProjectListRowDto
                {
                    Kind = ProjectListRowKind.Organization,
                    Organization = new ProjectListOrganizationHeaderDto
                    {
                        OrganizationId = project.OrganizationId,
                        Name = project.OrganizationName ?? "Unknown",
                        Color = project.OrganizationColor,
                        ProjectCount = projectCount
                    }
                };
            }

            if (project.ProjectGroupId == null)
            {
                return new ProjectListRowDto
                {
                    Kind = ProjectListRowKind.UnassignedBucket,
                    BucketLabel = "No Group",
                    ProjectCount = projectCount
                };
            }

            return new ProjectListRowDto
            {
                Kind = ProjectListRowKind.ProjectGroup,
                ProjectGroup = new ProjectListGroupHeaderDto
                {
                    ProjectGroupId = project.ProjectGroupId,
                    Name = project.ProjectGroupName ?? "Unknown",
                    Color = project.ProjectGroupColor,
                    ProjectCount = projectCount
                }
            };
        }

        private static IQueryable<ProjectDto> ProjectDtos(IQueryable<Project> query) =>
            query.Select(p => new ProjectDto
            {
                ProjectId = p.ProjectId,
                Name = p.Name,
                DisplayName = p.DisplayName,
                Slug = p.Slug,
                OrganizationId = p.OrganizationId,
                OrganizationName = p.Organization != null ? p.Organization.Name : null,
                OrganizationColor = p.Organization != null ? p.Organization.Color : null,
                DepartmentId = p.DepartmentId,
                DepartmentName = p.Department != null ? p.Department.Name : null,
                ProjectGroupId = p.ProjectGroupId,
                ProjectGroupName = p.ProjectGroup != null ? p.ProjectGroup.Name : null,
                ProjectGroupColor = p.ProjectGroup != null ? p.ProjectGroup.Color : null,
                IsActive = p.IsActive,
                IsArchived = p.IsArchived,
                IsSharedAvailability = p.IsSharedAvailability
            });

        private static ProjectListRowDto ProjectRow(ProjectDto project) =>
            new()
            {
                Kind = ProjectListRowKind.Project,
                Project = project
            };
    }
}
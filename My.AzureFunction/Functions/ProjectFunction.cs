using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.Shared.Constants;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.Project;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Models.Paging;
using My.DAL.Repository;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Shared.Validation;

namespace My.Functions
{
    public class ProjectFunctions
    {
        private readonly IRepository<Project> projectRepository;
        private readonly IRepository<Organization> organizationRepository;
        private readonly IRepository<AppSetting> appSettingRepository;
        private readonly IRepository<TrackedTask> taskRepository;
        private readonly ApplicationDbContext dbContext;
        private readonly AppMapper mapper;
        private readonly ILogger<ProjectFunctions> logger;
        private readonly IValidator<CreateProjectDto> createValidator;
        private readonly IValidator<UpdateProjectDto> updateValidator;

        public ProjectFunctions(
            IRepositoryFactory repositoryFactory,
            ApplicationDbContext dbContext,
            AppMapper mapper,
            ILogger<ProjectFunctions> logger,
            IValidator<CreateProjectDto> createValidator,
            IValidator<UpdateProjectDto> updateValidator)
        {
            this.mapper = mapper;
            this.logger = logger;
            this.createValidator = createValidator;
            this.updateValidator = updateValidator;
            this.dbContext = dbContext;
            projectRepository = repositoryFactory.GetRepository<Project>();
            organizationRepository = repositoryFactory.GetRepository<Organization>();
            appSettingRepository = repositoryFactory.GetRepository<AppSetting>();
            taskRepository = repositoryFactory.GetRepository<TrackedTask>();
        }

        /// <summary>
        /// True if any TrackedTask under this project has been synced to Google Calendar
        /// (i.e. has a GoogleEventId). Once a slug is "live" in someone's calendar, changing
        /// it would orphan those events from inbound sync, so we lock it.
        /// </summary>
        private async Task<bool> ProjectSlugIsLiveOnCalendarAsync(string projectId)
        {
            var live = await taskRepository.Get(t =>
                t.ProjectId == projectId && t.GoogleEventId != null);
            return live.Any();
        }

        [Function("GetProjects")]
        public async Task<IActionResult> GetProjectsAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "projects")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var listQuery = HttpListQueryParser.ParseListQuery(req);
            bool sharedAvailabilityOnly = bool.TryParse(req.Query["sharedAvailabilityOnly"], out var sa) && sa;
            var paging = HttpListQueryParser.ToPagingParameters(listQuery);

            if (!string.IsNullOrWhiteSpace(listQuery.GroupBy))
            {
                return new OkObjectResult(await ProjectGroupedListQuery.QueryAsync(
                    dbContext,
                    paging,
                    listQuery.IncludeArchived,
                    listQuery.IncludeInactive,
                    listQuery.GroupBy.Trim(),
                    listQuery.Search,
                    sharedAvailabilityOnly));
            }

            return new OkObjectResult(await QueryProjectsAsync(
                paging,
                listQuery.IncludeArchived,
                listQuery.IncludeInactive,
                sharedAvailabilityOnly));
        }

        [Function("LookupProjects")]
        public async Task<IActionResult> LookupProjectsAsync(
            // Single-segment route — see OrganizationFunctions.LookupOrganizationsAsync.
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "projectlookup")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var listQuery = HttpListQueryParser.ParseListQuery(req);
            listQuery.IncludeArchived = true;
            listQuery.IncludeInactive = true;
            if (listQuery.PageSize > 25)
                listQuery.PageSize = 25;

            return new OkObjectResult(await QueryProjectsAsync(
                HttpListQueryParser.ToPagingParameters(listQuery),
                includeArchived: true,
                includeInactive: true,
                sharedAvailabilityOnly: false));
        }

        private async Task<PagedResponse<ProjectDto>> QueryProjectsAsync(
            PagingParameters parameters,
            bool includeArchived,
            bool includeInactive,
            bool sharedAvailabilityOnly)
        {
            var filter = ProjectListFilters.Build(includeArchived, includeInactive, parameters.Search, sharedAvailabilityOnly);
            var orderBy = ProjectListFilters.OrderBy(parameters.SortBy, parameters.SortDescending);

            var pagedProjectList = await projectRepository.GetPaged(
                parameters,
                filter: filter,
                orderBy: orderBy,
                includeProperties: "ProjectGroup,Organization,Department");

            return new PagedResponse<ProjectDto>
            {
                Items = mapper.ProjectsToDtos(pagedProjectList),
                TotalCount = pagedProjectList.TotalCount,
                PageSize = pagedProjectList.PageSize,
                CurrentPage = pagedProjectList.CurrentPage,
                TotalPages = pagedProjectList.TotalPages,
                HasNext = pagedProjectList.HasNext,
                HasPrevious = pagedProjectList.HasPrevious
            };
        }

        [Function("GetProject")]
        public async Task<IActionResult> GetProjectAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "projects/{id}")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var project = await projectRepository.GetById(id);
            if (project == null)
                return new NotFoundObjectResult("Project not found!");

            return new OkObjectResult(mapper.ProjectToDto(project));
        }

        [Function("CreateProject")]
        public async Task<IActionResult> CreateProjectAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "projects")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            // Mutation requires Manager+ — GET endpoints stay open to any Tyme user so
            // regular users can browse projects in the picker without being able to edit.
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var (project, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, createValidator);
            if (validationError != null)
                return validationError;

            // Block if organization is inactive
            if (!string.IsNullOrEmpty(project!.OrganizationId))
            {
                var org = await organizationRepository.GetById(project.OrganizationId);
                if (org != null && !org.IsActive)
                    return new BadRequestObjectResult("Cannot create a project under an inactive organization.");
            }

            project.Slug = SlugRules.Normalize(project.Slug);
            if (project.Slug != null && !await IsSlugUniqueAsync(project.Slug, excludeProjectId: null))
                return new BadRequestObjectResult($"Slug \"{project.Slug}\" is already in use by another project.");

            if (project.IsSharedAvailability)
                project.IsBillable = false;

            var newProject = mapper.DtoToProject(project);
            await projectRepository.Insert(newProject);
            return new OkObjectResult(mapper.ProjectToDto(newProject));
        }

        /// <summary>
        /// Workspace-wide slug uniqueness. The slug is the calendar tag — `[slug]` in
        /// a Google Calendar event title routes to this project, so two projects can't
        /// share the slug regardless of group/org.
        /// </summary>
        private async Task<bool> IsSlugUniqueAsync(string slug, string? excludeProjectId)
        {
            return !await dbContext.Projects.AsNoTracking()
                .AnyAsync(p => p.Slug == slug && p.ProjectId != excludeProjectId);
        }

        [Function("GetProjectBillableImpact")]
        public async Task<IActionResult> GetProjectBillableImpactAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "projects/{id}/billableimpact")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var project = await projectRepository.GetById(id);
            if (project == null)
                return new NotFoundObjectResult("Project not found!");

            return new OkObjectResult(await ProjectBillableImpactQuery.QueryAsync(dbContext, id));
        }

        [Function("GetProjectDeleteImpact")]
        public async Task<IActionResult> GetProjectDeleteImpactAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "projects/{id}/deleteimpact")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var project = await projectRepository.GetById(id);
            if (project == null)
                return new NotFoundObjectResult("Project not found!");

            return new OkObjectResult(await ProjectDeleteImpactQuery.QueryAsync(dbContext, id));
        }

        [Function("DeleteProject")]
        public async Task<IActionResult> DeleteProjectAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "projects/{id}")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var setting = await appSettingRepository.GetById(Constants.SettingKeys.AllowProjectDelete);
            if (setting == null || !bool.TryParse(setting.Value, out var allowed) || !allowed)
                return new BadRequestObjectResult("Project deletion is not enabled in App Settings.");

            var projectToDelete = await projectRepository.GetById(id);
            if (projectToDelete == null)
                return new NotFoundObjectResult("Project not found!");

            var impact = await ProjectDeleteImpactQuery.QueryAsync(dbContext, id);
            if (!impact.CanDelete)
                return new BadRequestObjectResult(impact.BlockReason ?? "This project cannot be deleted because it has logged time or calendar history.");

            await projectRepository.Delete(projectToDelete);
            logger.LogInformation("Project {Id} {Name} was deleted.", projectToDelete.ProjectId, projectToDelete.Name);
            return new NoContentResult();
        }

        [Function("UpdateProject")]
        public async Task<IActionResult> UpdateProjectAsync([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "projects")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var (project, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, updateValidator);
            if (validationError != null)
                return validationError;

            var foundProject = await projectRepository.GetById(project!.ProjectId);
            if (foundProject == null)
                return new NotFoundObjectResult("Project not found!");

            // Block if project is inactive
            if (!foundProject.IsActive)
                return new BadRequestObjectResult("Cannot edit an inactive project.");

            // Block if organization is inactive
            if (!string.IsNullOrEmpty(project.OrganizationId))
            {
                var org = await organizationRepository.GetById(project.OrganizationId);
                if (org != null && !org.IsActive)
                    return new BadRequestObjectResult("Cannot assign a project to an inactive organization.");
            }

            project.Slug = SlugRules.Normalize(project.Slug);
            if (project.Slug != null && !await IsSlugUniqueAsync(project.Slug, project.ProjectId))
                return new BadRequestObjectResult($"Slug \"{project.Slug}\" is already in use by another project.");

            // Lock the slug only when an *existing* slug is being changed or removed —
            // those are the cases that orphan `[slug]` tags already typed into Google
            // Calendar event titles from the inbound-sync routing. Adding a slug to a
            // project that never had one is a strict improvement: there's no prior tag
            // to invalidate, and future events with the new slug will route correctly.
            //
            // Group changes are NOT locked anymore — calendar tags moved to single-token
            // `[slug]` (no more `[group-project]`), so a project's group affiliation no
            // longer affects calendar routing.
            var slugChanging = !string.IsNullOrEmpty(foundProject.Slug)
                && !string.Equals(foundProject.Slug, project.Slug, StringComparison.Ordinal);
            if (slugChanging && await ProjectSlugIsLiveOnCalendarAsync(foundProject.ProjectId))
                return new BadRequestObjectResult(
                    "This project's slug is locked because tracked time on it has been synced to Google Calendar. " +
                    "Changing the tag now would orphan those events. Disconnect Google Calendar (or delete the synced tasks) before renaming.");

            if (project.IsSharedAvailability)
                project.IsBillable = false;

            var wasBillable = foundProject.IsBillable;
            mapper.UpdateProjectFromDto(project, foundProject);

            if (foundProject.IsBillable != wasBillable)
                await ProjectBillableSync.SetTaskBillableFlagsAsync(dbContext, foundProject.ProjectId, foundProject.IsBillable);

            await projectRepository.Update(foundProject);
            return new OkObjectResult(mapper.ProjectToDto(foundProject));
        }

        [Function("SetActiveProject")]
        public async Task<IActionResult> SetActiveProjectAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "projects/{id}/setactive")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var project = await projectRepository.GetById(id);
            if (project == null)
                return new NotFoundObjectResult("Project not found!");

            project.IsActive = !project.IsActive;
            await projectRepository.Update(project);

            logger.LogInformation("Project {Id} IsActive set to {IsActive}", id, project.IsActive);
            return new OkObjectResult(new { project.IsActive });
        }

        [Function("ArchiveProject")]
        public async Task<IActionResult> ArchiveProjectAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "projects/{id}/archive")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var project = await projectRepository.GetById(id);
            if (project == null)
                return new NotFoundObjectResult("Project not found!");

            project.IsArchived = !project.IsArchived;
            if (project.IsArchived)
                project.IsActive = false;

            await projectRepository.Update(project);
            logger.LogInformation("Project {Id} IsArchived set to {IsArchived}", id, project.IsArchived);
            return new OkObjectResult(new { project.IsArchived, project.IsActive });
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Shared.Constants;
using My.Shared.Dtos.Analytics;
using My.Shared.Dtos.Dashboard;
using My.Shared.Rules;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Repository;
using My.Shared;

namespace My.Functions
{
    public class AnalyticsFunctions
    {
        private readonly IRepositoryFactory repositoryFactory;
        private readonly IRepository<TrackedTask> trackedTaskRepository;
        private readonly IRepository<TrackedTaskAlias> aliasRepository;
        private readonly IRepository<TimeSubmission> submissionRepository;
        private readonly ApplicationDbContext dbContext;
        private readonly ILogger<AnalyticsFunctions> logger;

        public AnalyticsFunctions(
            IRepositoryFactory repositoryFactory,
            ApplicationDbContext dbContext,
            ILogger<AnalyticsFunctions> logger)
        {
            this.logger = logger;
            this.dbContext = dbContext;
            this.repositoryFactory = repositoryFactory;
            trackedTaskRepository = repositoryFactory.GetRepository<TrackedTask>();
            aliasRepository = repositoryFactory.GetRepository<TrackedTaskAlias>();
            submissionRepository = repositoryFactory.GetRepository<TimeSubmission>();
        }

        /// <summary>
        /// Single endpoint that returns all dashboard data in one query.
        /// Loads only the last 12 months of tasks instead of the entire history.
        /// </summary>
        [Function("GetDashboard")]
        public async Task<IActionResult> GetDashboardAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analytics/dashboard")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            // Load last 12 months of data in a single DB query
            var now = DateTime.UtcNow;
            var twelveMonthsAgo = new DateTime(now.Year, now.Month, 1).AddMonths(-11).ToUniversalTime();

            var tasks = await trackedTaskRepository.Get(
                x => x.UserId == userId && x.StartDate >= twelveMonthsAgo,
                includeProperties: "Project.Organization,Project.ProjectGroup,Project.Department");

            var taskList = tasks.ToList();

            // This month / last month boundaries
            var thisMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastMonthStart = thisMonthStart.AddMonths(-1);

            var thisMonthTasks = taskList.Where(t => t.StartDate >= thisMonthStart).ToList();
            var lastMonthTasks = taskList.Where(t => t.StartDate >= lastMonthStart && t.StartDate < thisMonthStart).ToList();

            // Monthly bar chart (last 12 months)
            var monthlyChart = taskList
                .GroupBy(t => new { t.StartDate.Year, t.StartDate.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new WorkTimePerMonthDto
                {
                    Time = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("yyyy MMM"),
                    WorkTimeInSeconds = g.Sum(t => t.Duration.TotalSeconds)
                })
                .ToList();

            // Project doughnut chart (last 12 months) — enriched with parent IDs/names/colors
            // so the client can re-pivot by Organization or Project Group and color
            // segments by whichever axis is active without another round trip.
            var projectChart = taskList
                .GroupBy(t => new
                {
                    Id = t.ProjectId ?? "None",
                    Name = t.Project?.Name ?? "None",
                    OrgId = t.Project?.OrganizationId,
                    OrgName = t.Project?.Organization?.Name,
                    OrgColor = t.Project?.Organization?.Color,
                    GroupId = t.Project?.ProjectGroupId,
                    GroupName = t.Project?.ProjectGroup?.Name,
                    GroupColor = t.Project?.ProjectGroup?.Color
                })
                .Select(g => new ProjectDataItemDto
                {
                    ProjectId = g.Key.Id,
                    ProjectName = g.Key.Name,
                    Time = TimeSpan.FromSeconds(g.Sum(t => t.Duration.TotalSeconds)),
                    OrganizationId = g.Key.OrgId,
                    OrganizationName = g.Key.OrgName,
                    OrganizationColor = g.Key.OrgColor,
                    ProjectGroupId = g.Key.GroupId,
                    ProjectGroupName = g.Key.GroupName,
                    ProjectGroupColor = g.Key.GroupColor
                })
                .OrderByDescending(p => p.Time)
                .ToList();

            var dashboard = new DashboardDto
            {
                ThisMonth = BuildMonthSummary(thisMonthTasks),
                LastMonth = BuildMonthSummary(lastMonthTasks),
                MonthlyChart = monthlyChart,
                ProjectChart = projectChart
            };

            return new OkObjectResult(dashboard);
        }

        private AmountOfWorkTimeDto BuildMonthSummary(List<TrackedTask> tasks)
        {
            double totalSeconds = tasks.Sum(t => t.Duration.TotalSeconds);
            var (topName, topSeconds) = FindTopProject(tasks);

            return new AmountOfWorkTimeDto
            {
                AmountWorkTime = totalSeconds,
                AmountWorkTimeText = GetAmountWorkTimeFormatted(totalSeconds),
                TopProject = topName,
                TopProjectAmounTime = topSeconds,
                TopProjectAmounTimeText = GetAmountWorkTimeFormatted(topSeconds)
            };
        }

        /// <summary>
        /// Manager employee-picker source: active Tyme-scoped users the caller can manage,
        /// independent of whether they have tasks in a date range.
        /// </summary>
        [Function("GetManageableEmployees")]
        public async Task<IActionResult> GetManageableEmployeesAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analytics/manageableemployees")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var users = await dbContext.ApplicationUsers
                .Where(u => u.IsActive && !u.IsArchived)
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
                .ToListAsync();

            if (users.Count == 0)
                return new OkObjectResult(Array.Empty<ManageableEmployeeDto>());

            var userIds = users.Select(u => u.Id).ToList();
            var roleRows = await (from ur in dbContext.UserRoles
                                  where userIds.Contains(ur.UserId)
                                  join r in dbContext.Roles on ur.RoleId equals r.Id
                                  select new { ur.UserId, RoleName = r.Name! })
                                 .ToListAsync();
            var rolesByUser = roleRows
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => (IList<string>)g.Select(x => x.RoleName).ToList());

            var result = new List<ManageableEmployeeDto>(users.Count);
            foreach (var user in users)
            {
                var roles = rolesByUser.TryGetValue(user.Id, out var rs) ? rs : Array.Empty<string>();
                if (!Constants.Roles.IsVisibleInTymeTeamView(principal, roles)) continue;

                result.Add(new ManageableEmployeeDto
                {
                    UserId = user.Id,
                    UserName = UserDisplayNameRules.Resolve(user.FirstName, user.LastName, user.Email)
                });
            }

            return new OkObjectResult(result.OrderBy(u => u.UserName).ToList());
        }

        /// <summary>
        /// Admin:Tyme entity-centric table extract — requested datasets only, with optional scope filters.
        /// </summary>
        [Function("GetTymeDataExtraction")]
        public async Task<IActionResult> GetTymeDataExtractionAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analytics/dataextraction")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, Constants.Roles.Admin) is IActionResult unauth)
                return unauth;

            if (!TymeDataExtractionRules.TryParseEntities(req.Query["Entities"], out var entities, out var parseError))
                return new BadRequestObjectResult(parseError);

            DateTime? from = DateTime.TryParse(req.Query["From"], out var f) ? f.ToUniversalTime() : null;
            DateTime? to = DateTime.TryParse(req.Query["To"], out var t) ? t.ToUniversalTime().AddDays(1).AddTicks(-1) : null;

            if (TymeDataExtractionRules.ValidateRequest(entities, from, to) is { } validationError)
                return new BadRequestObjectResult(validationError);

            var includeArchived = bool.TryParse(req.Query["IncludeArchived"], out var archived) && archived;
            var organizationId = string.IsNullOrWhiteSpace(req.Query["OrganizationId"]) ? null : req.Query["OrganizationId"];
            var projectGroupId = string.IsNullOrWhiteSpace(req.Query["ProjectGroupId"]) ? null : req.Query["ProjectGroupId"];
            var projectId = string.IsNullOrWhiteSpace(req.Query["ProjectId"]) ? null : req.Query["ProjectId"];

            var userIds = new HashSet<string>(StringComparer.Ordinal);
            var rawUserIds = req.Query["UserIds"];
            if (!string.IsNullOrWhiteSpace(rawUserIds))
            {
                foreach (var id in rawUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    userIds.Add(id);
            }

            var builder = new Helpers.TymeDataExtractionBuilder(dbContext, repositoryFactory);
            var export = await builder.BuildAsync(new Helpers.TymeDataExtractionRequest
            {
                Entities = entities,
                FromUtc = from,
                ToUtc = to,
                IncludeArchived = includeArchived,
                OrganizationId = organizationId,
                ProjectGroupId = projectGroupId,
                ProjectId = projectId,
                UserIds = userIds
            });

            return new OkObjectResult(export);
        }

        [Function("GetAllUsersTrackedTasks")]
        public async Task<IActionResult> GetAllUsersTrackedTasksAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analytics/alluserstasks")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            DateTime? from = DateTime.TryParse(req.Query["From"], out var f) ? f.ToUniversalTime() : null;
            DateTime? to = DateTime.TryParse(req.Query["To"], out var t) ? t.ToUniversalTime().AddDays(1).AddTicks(-1) : null;

            var tasks = (await trackedTaskRepository.Get(
                filter: task =>
                    (from == null || task.StartDate >= from) &&
                    (to == null || task.StartDate <= to),
                includeProperties: "Project.Organization,Project.ProjectGroup,User")).ToList();

            // Overlay manager-created aliases on top of the originals.
            var taskIds = tasks.Select(t => t.TaskId).ToList();
            var aliases = (await aliasRepository.Get(
                a => taskIds.Contains(a.TaskId),
                includeProperties: "Project.Organization,Project.ProjectGroup")).ToList();
            var aliasByTaskId = aliases.ToDictionary(a => a.TaskId);

            var audits = await dbContext.TrackedTaskCorrectionAudits.AsNoTracking()
                .Where(a => taskIds.Contains(a.TaskId))
                .ToListAsync();
            var auditByTaskId = audits.ToDictionary(a => a.TaskId);
            var prevProjectIds = audits
                .Where(a => !string.IsNullOrEmpty(a.PreviousProjectId))
                .Select(a => a.PreviousProjectId!)
                .Distinct()
                .ToList();
            var prevProjectNames = prevProjectIds.Count == 0
                ? new Dictionary<string, string>()
                : await dbContext.Projects.AsNoTracking()
                    .Where(p => prevProjectIds.Contains(p.ProjectId))
                    .ToDictionaryAsync(p => p.ProjectId, p => p.Name);

            // Submission lookup to mark each row as IsMonthSubmitted (controls who can edit).
            var userIds = tasks.Select(x => x.UserId).Distinct().ToList();
            var submissions = (await submissionRepository.Get(
                s => userIds.Contains(s.UserId))).ToList();
            var submittedSet = submissions
                .Select(s => (s.UserId, s.Year, s.Month))
                .ToHashSet();

            var rolesByUser = await LoadRolesByUserAsync(userIds);

            var result = tasks.Where(task =>
            {
                var roles = rolesByUser.TryGetValue(task.UserId, out var rs) ? rs : Array.Empty<string>();
                return Constants.Roles.IsVisibleInTymeTeamView(principal, roles);
            }).Select(task =>
            {
                aliasByTaskId.TryGetValue(task.TaskId, out var alias);
                auditByTaskId.TryGetValue(task.TaskId, out var audit);
                var effectiveName = alias?.Name ?? task.Name;
                var effectiveStart = alias?.StartDate ?? task.StartDate;
                var effectiveDuration = alias?.Duration ?? task.Duration;
                var effectiveProject = alias?.Project ?? task.Project;
                var effectiveProjectId = alias?.ProjectId ?? task.ProjectId;
                var effectiveIsBillable = alias?.IsBillable ?? task.IsBillable;
                var monthSubmitted = submittedSet.Contains((task.UserId, task.StartDate.Year, task.StartDate.Month));

                return new
                {
                    task.TaskId,
                    Name = effectiveName,
                    Duration = effectiveDuration.TotalSeconds,
                    StartDate = effectiveStart,
                    EndDate = effectiveDuration > TimeSpan.Zero ? (DateTime?)(effectiveStart + effectiveDuration) : null,
                    ProjectName = effectiveProject?.Name ?? "None",
                    ProjectSlug = effectiveProject?.Slug,
                    ProjectId = effectiveProjectId,
                    OrganizationName = effectiveProject?.Organization?.Name,
                    OrganizationId = effectiveProject?.OrganizationId,
                    ProjectGroupName = effectiveProject?.ProjectGroup?.Name,
                    ProjectGroupId = effectiveProject?.ProjectGroupId,
                    UserName = UserDisplayNameRules.Resolve(task.User?.FirstName, task.User?.LastName, task.User?.Email),
                    task.UserId,
                    IsBillable = effectiveIsBillable,
                    IsAliased = alias != null,
                    IsDirectCorrected = audit != null,
                    IsMonthSubmitted = monthSubmitted,
                    OriginalStartDate = alias != null
                        ? (DateTime?)task.StartDate
                        : audit != null ? audit.PreviousStartDate : null,
                    OriginalDurationSeconds = alias != null
                        ? (double?)task.Duration.TotalSeconds
                        : audit != null ? audit.PreviousDuration.TotalSeconds : null,
                    OriginalProjectId = alias != null
                        ? task.ProjectId
                        : audit?.PreviousProjectId,
                    OriginalProjectName = alias != null
                        ? (task.Project?.Name ?? "None")
                        : audit != null
                            ? (audit.PreviousProjectId != null
                                && prevProjectNames.TryGetValue(audit.PreviousProjectId, out var prevName)
                                ? prevName
                                : "None")
                            : null,
                    OriginalName = alias != null
                        ? task.Name
                        : audit?.PreviousName,
                    OriginalIsBillable = alias != null
                        ? (bool?)task.IsBillable
                        : audit != null ? audit.PreviousIsBillable : null
                };
            }).OrderByDescending(t => t.StartDate);

            return new OkObjectResult(result);
        }

        private async Task<Dictionary<string, IList<string>>> LoadRolesByUserAsync(IReadOnlyCollection<string> userIds)
        {
            if (userIds.Count == 0)
                return new Dictionary<string, IList<string>>();

            var roleRows = await (from ur in dbContext.UserRoles
                                  where userIds.Contains(ur.UserId)
                                  join r in dbContext.Roles on ur.RoleId equals r.Id
                                  select new { ur.UserId, RoleName = r.Name! })
                                 .ToListAsync();

            return roleRows
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => (IList<string>)g.Select(x => x.RoleName).ToList());
        }

        private static (string Name, double Seconds) FindTopProject(IEnumerable<TrackedTask> tasks)
        {
            var top = tasks
                .GroupBy(t => t.ProjectId)
                .Select(g => new
                {
                    Name = g.First().Project?.Name ?? "None",
                    Seconds = g.Sum(t => t.Duration.TotalSeconds)
                })
                .OrderByDescending(p => p.Seconds)
                .FirstOrDefault();

            return top != null ? (top.Name, top.Seconds) : ("None", 0);
        }

        private static string GetAmountWorkTimeFormatted(double secondsSum)
        {
            TimeSpan totalTime = TimeSpan.FromSeconds(secondsSum);
            int hours = (totalTime.Days * 24) + totalTime.Hours;
            string amountWorkTimeLastMonthText = $"{hours}:{totalTime.Minutes:00}:{totalTime.Seconds:00}";
            return amountWorkTimeLastMonthText;
        }
    }
}

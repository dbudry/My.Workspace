using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using My.DAL.Data;
using My.DAL.Models;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Shared.Dtos.StopwatchItem;
using My.Shared.Rules;

namespace My.Functions
{
    /// <summary>
    /// Backs the unified Tasks page: merges stopwatch work items and manuals, then pages
    /// without loading full history. Manuals come from <see cref="ITaskListManualStore"/>
    /// (EF today; Azure Table later); merge math is pure <see cref="TaskListRules"/>.
    /// </summary>
    public class TaskListFunctions
    {
        private readonly ApplicationDbContext dbContext;
        private readonly AppMapper mapper;

        public TaskListFunctions(ApplicationDbContext dbContext, AppMapper mapper)
        {
            this.dbContext = dbContext;
            this.mapper = mapper;
        }

        [Function("GetTaskList")]
        public async Task<IActionResult> GetTaskListAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tasklist")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var query = HttpListQueryParser.ParseListQuery(req);
            var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            var pageSize = query.EffectivePageSize;
            var prefixLen = TaskListRules.RequiredManualPrefixLength(pageNumber, pageSize);

            var store = new EfTaskListManualStore(dbContext, mapper);

            // Count + ordered prefix only — never Take(10000) of the user's full career.
            var totalManuals = await store.CountAsync(userId, query.Search);
            var manualDtos = (await store.GetOrderedPrefixAsync(
                userId,
                query.Search,
                query.SortBy,
                query.SortDescending,
                prefixLen)).ToList();

            if (manualDtos.Count > 0)
            {
                var taskIds = manualDtos.Select(t => t.TaskId).ToList();
                var adjustmentContext = await TrackedTaskAdjustmentEnricher.LoadForTasksAsync(dbContext, taskIds);
                // Re-load entities only if enricher needs them — we already have DTOs; enrich by id.
                for (var i = 0; i < manualDtos.Count; i++)
                {
                    var id = manualDtos[i].TaskId;
                    adjustmentContext.Aliases.TryGetValue(id, out var alias);
                    adjustmentContext.Audits.TryGetValue(id, out var audit);
                    TrackedTaskAdjustmentEnricher.ApplyEmployeeView(
                        manualDtos[i], alias, audit, adjustmentContext, mapper);
                }

                var submitted = await GetSubmittedMonthsAsync(userId);
                foreach (var dto in manualDtos)
                    dto.IsMonthSubmitted = submitted.Contains((dto.StartDate.Year, dto.StartDate.Month));
            }

            // Stopwatch work items are few (one row per work item) — load all for this user.
            var stopwatchDtos = await LoadStopwatchDtosAsync(userId);

            var page = TaskListRules.BuildPageFromManualPrefix(
                stopwatchDtos,
                manualDtos,
                totalMatchingManuals: totalManuals,
                search: query.Search,
                sortBy: query.SortBy,
                sortDescending: query.SortDescending,
                pageNumber: pageNumber,
                pageSize: pageSize,
                nowUtc: DateTime.UtcNow);

            return new OkObjectResult(page);
        }

        private async Task<HashSet<(int Year, int Month)>> GetSubmittedMonthsAsync(string userId)
        {
            var rows = await dbContext.TimeSubmissions.AsNoTracking()
                .Where(s => s.UserId == userId)
                .Select(s => new { s.Year, s.Month })
                .ToListAsync();
            return rows.Select(s => (s.Year, s.Month)).ToHashSet();
        }

        private async Task<List<StopwatchItemDto>> LoadStopwatchDtosAsync(string userId)
        {
            var items = await dbContext.StopwatchItems.AsNoTracking()
                .Where(i => i.UserId == userId)
                .Include(i => i.Project!).ThenInclude(p => p.ProjectGroup)
                .Include(i => i.Project!).ThenInclude(p => p.Organization)
                .ToListAsync();

            if (items.Count == 0) return new List<StopwatchItemDto>();

            var itemIds = items.Select(i => i.StopwatchItemId).ToList();
            var sessions = await dbContext.TrackedTasks.AsNoTracking()
                .Where(t => t.StopwatchItemId != null && itemIds.Contains(t.StopwatchItemId))
                .ToListAsync();
            var byItem = sessions.GroupBy(t => t.StopwatchItemId!).ToDictionary(g => g.Key, g => g.ToList());
            var submitted = await GetSubmittedMonthsAsync(userId);

            return items
                .Select(i => ToStopwatchDto(
                    i,
                    byItem.GetValueOrDefault(i.StopwatchItemId) ?? new List<TrackedTask>(),
                    submitted))
                .ToList();
        }

        private StopwatchItemDto ToStopwatchDto(
            StopwatchItem item,
            List<TrackedTask> sessions,
            HashSet<(int Year, int Month)> submittedMonths)
        {
            var active = sessions.FirstOrDefault(t => t.EndDate == null);
            var completedTotal = sessions
                .Where(t => t.EndDate != null)
                .Aggregate(TimeSpan.Zero, (sum, t) => sum + t.Duration);
            var hasLocked = sessions.Any(s =>
                submittedMonths.Contains((s.StartDate.Year, s.StartDate.Month)));

            return new StopwatchItemDto
            {
                StopwatchItemId = item.StopwatchItemId,
                Name = item.Name,
                ProjectId = item.ProjectId,
                Project = item.Project == null ? null : mapper.ProjectToDto(item.Project),
                TotalDuration = completedTotal,
                IsRunning = active != null,
                ActiveSessionId = active?.TaskId,
                ActiveSessionStartDate = active?.StartDate,
                LastWorkedAt = item.LastWorkedAt,
                CreatedAt = item.CreatedAt,
                HasLockedSessions = hasLocked
            };
        }
    }
}

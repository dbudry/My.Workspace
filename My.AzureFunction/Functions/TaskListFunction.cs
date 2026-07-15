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
    /// Backs the unified Tasks page: merges the user's stopwatch work items and manual tracked
    /// tasks, then sorts, filters, and pages them server-side so the client makes one request per
    /// page instead of pulling every row. Lives on its own top-level route so it can't collide
    /// with trackedtasks/{id}.
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

            // Manual entries (exclude stopwatch sessions) — one capped round-trip, not per-page loops.
            var manualTasks = await TrackedTaskRangeQuery.LoadAsync(
                dbContext, userId, from: null, to: null, search: null, excludeStopwatchSessions: true);
            var submitted = await GetSubmittedMonthsAsync(userId);
            var manualDtos = manualTasks.Select(t =>
            {
                var dto = mapper.TrackedTaskToDto(t);
                dto.IsMonthSubmitted = submitted.Contains((t.StartDate.Year, t.StartDate.Month));
                return dto;
            }).ToList();

            if (manualDtos.Count > 0)
            {
                var taskIds = manualTasks.Select(t => t.TaskId).ToList();
                var adjustmentContext = await TrackedTaskAdjustmentEnricher.LoadForTasksAsync(dbContext, taskIds);
                for (var i = 0; i < manualDtos.Count; i++)
                {
                    adjustmentContext.Aliases.TryGetValue(manualTasks[i].TaskId, out var alias);
                    adjustmentContext.Audits.TryGetValue(manualTasks[i].TaskId, out var audit);
                    TrackedTaskAdjustmentEnricher.ApplyEmployeeView(
                        manualDtos[i], alias, audit, adjustmentContext, mapper);
                }
            }

            // Stopwatch work items are few (one per work item) — load all and compute their totals.
            var stopwatchDtos = await LoadStopwatchDtosAsync(userId);

            var page = TaskListRules.BuildPage(
                stopwatchDtos,
                manualDtos,
                search: query.Search,
                sortBy: query.SortBy,
                sortDescending: query.SortDescending,
                pageNumber: query.PageNumber,
                pageSize: query.PageSize,
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

            return items
                .Select(i => ToStopwatchDto(i, byItem.GetValueOrDefault(i.StopwatchItemId) ?? new List<TrackedTask>()))
                .ToList();
        }

        // Mirrors StopwatchItemFunction.ToListDto: total is the sum of completed session durations,
        // running is whether an open (EndDate == null) session exists.
        private StopwatchItemDto ToStopwatchDto(StopwatchItem item, List<TrackedTask> sessions)
        {
            var active = sessions.FirstOrDefault(t => t.EndDate == null);
            var completedTotal = sessions
                .Where(t => t.EndDate != null)
                .Aggregate(TimeSpan.Zero, (sum, t) => sum + t.Duration);

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
                CreatedAt = item.CreatedAt
            };
        }
    }
}

using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.DAL.Data;
using My.DAL.Models;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Functions.Services;
using My.Shared.Constants;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;

namespace My.Functions;

public class ManagerTimeCorrectionFunction
{
    private readonly ApplicationDbContext _dbContext;
    private readonly AppMapper _mapper;
    private readonly GoogleCalendarService _googleCalendar;
    private readonly TeamAvailabilityPublisher _teamAvailabilityPublisher;
    private readonly ILogger<ManagerTimeCorrectionFunction> _logger;
    private readonly IValidator<ManagerTimeCorrectionDto> _validator;

    public ManagerTimeCorrectionFunction(
        ApplicationDbContext dbContext,
        AppMapper mapper,
        GoogleCalendarService googleCalendar,
        TeamAvailabilityPublisher teamAvailabilityPublisher,
        ILogger<ManagerTimeCorrectionFunction> logger,
        IValidator<ManagerTimeCorrectionDto> validator)
    {
        _dbContext = dbContext;
        _mapper = mapper;
        _googleCalendar = googleCalendar;
        _teamAvailabilityPublisher = teamAvailabilityPublisher;
        _logger = logger;
        _validator = validator;
    }

    [Function("UpsertManagerTimeCorrection")]
    public async Task<IActionResult> UpsertAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "trackedtasks/{taskId}/manager-correction")] HttpRequestData req,
        string taskId)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedTyme(principal, out var actorId, Constants.Roles.Manager) is IActionResult unauth)
            return unauth;

        var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, _validator);
        if (validationError != null)
            return validationError;

        var task = await _dbContext.TrackedTasks
            .Include(t => t.Project)
            .FirstOrDefaultAsync(t => t.TaskId == taskId);
        if (task == null)
            return new NotFoundObjectResult("Tracked task not found.");

        if (!await CanManageTaskOwnerAsync(principal, task.UserId))
            return new ForbidResult();

        if (await _dbContext.TrackedTaskAliases.AnyAsync(a => a.TaskId == taskId))
            return new BadRequestObjectResult("This task has an alias correction. Direct edits are not allowed on the same task.");

        var taskMonthSubmitted = await _dbContext.TimeSubmissions
            .AnyAsync(s => s.UserId == task.UserId
                        && s.Year == task.StartDate.Year
                        && s.Month == task.StartDate.Month);

        var correctionSettings = await ManagerCorrectionSettingsLoader.LoadAsync(_dbContext);

        var existingAudit = await _dbContext.TrackedTaskCorrectionAudits
            .FirstOrDefaultAsync(a => a.TaskId == taskId);

        var settingsDecision = ManagerCorrectionRules.Evaluate(
            ManagerCorrectionRules.CorrectionMode.Direct,
            existingAudit == null
                ? ManagerCorrectionRules.CorrectionAction.Create
                : ManagerCorrectionRules.CorrectionAction.Update,
            correctionSettings);
        if (!settingsDecision.IsAllowed)
            return new BadRequestObjectResult(settingsDecision.Reason!);

        if (existingAudit == null)
        {
            var createDecision = SubmissionRules.Evaluate(SubmissionRules.Operation.ManagerDirectEdit, taskMonthSubmitted);
            if (!createDecision.IsAllowed)
                return new BadRequestObjectResult(createDecision.Reason!);
        }

        if (!string.IsNullOrEmpty(body!.ProjectId))
        {
            var projectIssue = await ValidateProjectIsLoggableAsync(body.ProjectId);
            if (projectIssue != null)
                return new BadRequestObjectResult(projectIssue);
        }

        var startUtc = body.StartDate.Kind == DateTimeKind.Utc
            ? body.StartDate
            : body.StartDate.ToUniversalTime();
        var nowUtc = DateTime.UtcNow;
        var newProjectId = string.IsNullOrEmpty(body.ProjectId) ? null : body.ProjectId;

        if (existingAudit == null)
        {
            existingAudit = new TrackedTaskCorrectionAudit
            {
                TaskId = taskId,
                CorrectedByUserId = actorId,
                CorrectedAtUtc = nowUtc,
                PreviousName = task.Name,
                PreviousStartDate = task.StartDate,
                PreviousDuration = task.Duration,
                PreviousProjectId = task.ProjectId,
                PreviousIsBillable = task.IsBillable,
                NewName = body.Name.Trim(),
                NewStartDate = startUtc,
                NewDuration = body.Duration,
                NewProjectId = newProjectId,
                NewIsBillable = body.IsBillable
            };
            _dbContext.TrackedTaskCorrectionAudits.Add(existingAudit);
            _logger.LogInformation("User {ActorId} created direct correction for task {TaskId}.", actorId, taskId);
        }
        else
        {
            existingAudit.CorrectedByUserId = actorId;
            existingAudit.CorrectedAtUtc = nowUtc;
            existingAudit.NewName = body.Name.Trim();
            existingAudit.NewStartDate = startUtc;
            existingAudit.NewDuration = body.Duration;
            existingAudit.NewProjectId = newProjectId;
            existingAudit.NewIsBillable = body.IsBillable;
            _logger.LogInformation("User {ActorId} updated direct correction for task {TaskId}.", actorId, taskId);
        }

        task.Name = existingAudit.NewName;
        task.StartDate = existingAudit.NewStartDate;
        task.Duration = existingAudit.NewDuration;
        task.ProjectId = existingAudit.NewProjectId;
        task.IsBillable = existingAudit.NewIsBillable;
        if (task.Duration > TimeSpan.Zero)
            task.EndDate = task.StartDate + task.Duration;

        await _dbContext.SaveChangesAsync();
        await TryPushUpdateAsync(task);

        await _dbContext.Entry(task).Reference(t => t.Project).LoadAsync();
        var dto = _mapper.TrackedTaskToDto(task);
        dto.IsMonthSubmitted = taskMonthSubmitted;
        dto.IsManagerAdjusted = true;
        dto.AdjustmentKind = "Direct";
        return new OkObjectResult(dto);
    }

    [Function("DeleteManagerTimeCorrection")]
    public async Task<IActionResult> DeleteAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "trackedtasks/{taskId}/manager-correction")] HttpRequestData req,
        string taskId)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedTyme(principal, out var actorId, Constants.Roles.Manager) is IActionResult unauth)
            return unauth;

        var task = await _dbContext.TrackedTasks.FirstOrDefaultAsync(t => t.TaskId == taskId);
        if (task == null)
            return new NotFoundObjectResult("Tracked task not found.");

        if (!await CanManageTaskOwnerAsync(principal, task.UserId))
            return new ForbidResult();

        var audit = await _dbContext.TrackedTaskCorrectionAudits
            .FirstOrDefaultAsync(a => a.TaskId == taskId);
        if (audit == null)
            return new NotFoundObjectResult("Direct correction not found.");

        task.Name = audit.PreviousName;
        task.StartDate = audit.PreviousStartDate;
        task.Duration = audit.PreviousDuration;
        task.ProjectId = audit.PreviousProjectId;
        task.IsBillable = audit.PreviousIsBillable;
        task.EndDate = task.Duration > TimeSpan.Zero ? task.StartDate + task.Duration : null;

        _dbContext.TrackedTaskCorrectionAudits.Remove(audit);
        await _dbContext.SaveChangesAsync();
        await TryPushUpdateAsync(task);

        _logger.LogInformation("User {ActorId} reverted direct correction for task {TaskId}.", actorId, taskId);
        return new NoContentResult();
    }

    private async Task<bool> CanManageTaskOwnerAsync(ClaimsPrincipal principal, string taskUserId)
    {
        var roleRows = await (from ur in _dbContext.UserRoles
                              where ur.UserId == taskUserId
                              join r in _dbContext.Roles on ur.RoleId equals r.Id
                              select r.Name!)
                             .ToListAsync();
        return Constants.Roles.IsVisibleInTymeTeamView(principal, roleRows);
    }

    private async Task<string?> ValidateProjectIsLoggableAsync(string projectId)
    {
        var project = await _dbContext.Projects
            .Include(p => p.Organization)
            .Include(p => p.Department)
            .FirstOrDefaultAsync(p => p.ProjectId == projectId);
        if (project == null) return "Project not found.";
        if (project.IsArchived) return "Cannot correct to an archived project.";
        if (!project.IsActive) return "Cannot correct to an inactive project.";
        if (project.Organization is { IsArchived: true })
            return "Cannot correct to a project whose organization is archived.";
        if (project.Organization is { IsActive: false })
            return "Cannot correct to a project whose organization is inactive.";
        if (project.Department is { IsArchived: true })
            return "Cannot correct to a project whose department is archived.";
        if (project.Department is { IsActive: false })
            return "Cannot correct to a project whose department is inactive.";
        return null;
    }

    private async Task TryPushUpdateAsync(TrackedTask task)
    {
        try
        {
            var settings = await _dbContext.UserSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == task.UserId);
            if (settings == null || string.IsNullOrEmpty(settings.GoogleRefreshToken) || string.IsNullOrEmpty(settings.GoogleCalendarId))
            {
                await _teamAvailabilityPublisher.PublishAsync(task, settings);
                return;
            }

            var project = string.IsNullOrEmpty(task.ProjectId)
                ? null
                : await _dbContext.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectId == task.ProjectId);

            if (!string.IsNullOrEmpty(task.GoogleEventId) && settings.PublishToGoogleCalendar)
            {
                await _googleCalendar.UpdateEventAsync(
                    settings.GoogleRefreshToken,
                    settings.GoogleCalendarId,
                    task.GoogleEventId,
                    task,
                    project?.Slug,
                    settings.TimeZone,
                    settings.TymeEventColorId,
                    settings.TymeUnmatchedEventColorId);
            }

            await _teamAvailabilityPublisher.PublishAsync(task, settings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push direct correction for TrackedTask {TaskId} to Google.", task.TaskId);
        }
    }
}
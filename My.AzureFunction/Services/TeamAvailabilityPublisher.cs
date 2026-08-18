using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Repository;
using My.Shared.Constants;
using My.Shared.Rules;

namespace My.Functions.Services;

/// <summary>
/// Reconciles the workspace-shared "Team Availability" Google calendar sister-event
/// against a TrackedTask. Pulled out of TrackedTaskFunction so the inbound sync path
/// (GoogleCalendarFunction.ImportChangesAsync) can dual-publish too — when a user
/// adds an [ooo] event on their primary calendar, it needs to fan out to the team
/// calendar the same way an in-app save would.
///
/// Handles all four transitions (off→off, off→on, on→on, on→off) including stale-id
/// recreate. Errors are logged but never bubble — the primary task save must not
/// fail because the team calendar push glitched.
/// </summary>
public class TeamAvailabilityPublisher
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IRepository<TrackedTask> _taskRepository;
    private readonly IRepository<Project> _projectRepository;
    private readonly IRepository<UserSettings> _settingsRepository;
    private readonly IRepository<ApplicationUser> _userRepository;
    private readonly GoogleCalendarService _googleCalendar;
    private readonly ILogger<TeamAvailabilityPublisher> _logger;

    public TeamAvailabilityPublisher(
        ApplicationDbContext dbContext,
        IRepositoryFactory repositoryFactory,
        GoogleCalendarService googleCalendar,
        ILogger<TeamAvailabilityPublisher> logger)
    {
        _dbContext = dbContext;
        _taskRepository = repositoryFactory.GetRepository<TrackedTask>();
        _projectRepository = repositoryFactory.GetRepository<Project>();
        _settingsRepository = repositoryFactory.GetRepository<UserSettings>();
        _userRepository = repositoryFactory.GetRepository<ApplicationUser>();
        _googleCalendar = googleCalendar;
        _logger = logger;
    }

    /// <summary>
    /// Reconciles the sister event for the given task. Caller may pass <paramref name="settings"/>
    /// to avoid an extra DB lookup when the user's settings are already in hand.
    /// </summary>
    public async Task PublishAsync(TrackedTask task, UserSettings? settings = null)
    {
        try
        {
            var teamCalId = await GetTeamAvailabilityCalendarIdAsync();
            if (teamCalId == null) return;

            settings ??= (await _settingsRepository.Get(s => s.UserId == task.UserId)).FirstOrDefault();
            if (settings == null || string.IsNullOrEmpty(settings.GoogleRefreshToken)) return;

            var project = string.IsNullOrEmpty(task.ProjectId)
                ? null
                : await _projectRepository.GetById(task.ProjectId);
            var shouldHaveEvent = project?.IsSharedAvailability == true;

            if (!shouldHaveEvent)
            {
                if (string.IsNullOrEmpty(task.TeamAvailabilityEventId)) return;
                try
                {
                    await _googleCalendar.DeleteEventAsync(settings.GoogleRefreshToken, teamCalId, task.TeamAvailabilityEventId);
                }
                catch (Google.GoogleApiException ex) when (
                    ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound ||
                    ex.HttpStatusCode == System.Net.HttpStatusCode.Gone)
                {
                    // Already gone from Google — just drop the linkage.
                }
                task.TeamAvailabilityEventId = null;
                await _taskRepository.Update(task);
                return;
            }

            var user = await _userRepository.GetById(task.UserId);
            var displayName = UserDisplayNameRules.Resolve(user?.FirstName, user?.LastName, user?.Email);
            // Public-facing label: prefer the explicit override, otherwise fall back to
            // the generic "Out of Office". Falling back to project.Name (the original
            // behavior) leaked internal names like "Vacation Q2" onto the team calendar.
            var projectLabel = TeamAvailabilityEventRules.ResolveDisplayName(project!.DisplayName);

            if (!string.IsNullOrEmpty(task.TeamAvailabilityEventId))
            {
                try
                {
                    await _googleCalendar.UpdateTeamAvailabilityEventAsync(
                        settings.GoogleRefreshToken, teamCalId, task.TeamAvailabilityEventId,
                        task, displayName, projectLabel, settings.TimeZone);
                    return;
                }
                catch (Google.GoogleApiException ex) when (
                    ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound ||
                    ex.HttpStatusCode == System.Net.HttpStatusCode.Gone)
                {
                    _logger.LogInformation(
                        "Stale team-availability event {EventId} for TrackedTask {TaskId} — recreating.",
                        task.TeamAvailabilityEventId, task.TaskId);
                    task.TeamAvailabilityEventId = null;
                }
            }

            var created = await _googleCalendar.CreateTeamAvailabilityEventAsync(
                settings.GoogleRefreshToken, teamCalId, task,
                displayName, projectLabel, settings.TimeZone);
            task.TeamAvailabilityEventId = created.Id;
            await _taskRepository.Update(task);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dual-publish TrackedTask {TaskId} to team availability calendar.", task.TaskId);
        }
    }

    /// <summary>
    /// Removes the sister event from the workspace Team Availability calendar — used
    /// when the underlying TrackedTask is being deleted entirely (either by the user
    /// in-app or by Google webhook reporting the primary-calendar event was cancelled).
    /// Distinct from <see cref="PublishAsync"/>'s "shouldn't have event" branch, which
    /// runs when the task remains but its project flag flipped off.
    /// </summary>
    public async Task DeleteSisterEventAsync(TrackedTask task, UserSettings? settings = null)
    {
        if (string.IsNullOrEmpty(task.TeamAvailabilityEventId)) return;

        try
        {
            var teamCalId = await GetTeamAvailabilityCalendarIdAsync();
            if (teamCalId == null) return;

            settings ??= (await _settingsRepository.Get(s => s.UserId == task.UserId)).FirstOrDefault();
            if (settings == null || string.IsNullOrEmpty(settings.GoogleRefreshToken)) return;

            await _googleCalendar.DeleteEventAsync(settings.GoogleRefreshToken, teamCalId, task.TeamAvailabilityEventId);
        }
        catch (Google.GoogleApiException ex) when (
            ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound ||
            ex.HttpStatusCode == System.Net.HttpStatusCode.Gone)
        {
            // Already gone from Google — nothing to clean up.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete team-availability event for TrackedTask {TaskId}.", task.TaskId);
        }
    }

    private async Task<string?> GetTeamAvailabilityCalendarIdAsync()
    {
        var row = await _dbContext.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == Constants.SettingKeys.TeamAvailabilityCalendarId);
        return string.IsNullOrWhiteSpace(row?.Value) ? null : row.Value;
    }
}

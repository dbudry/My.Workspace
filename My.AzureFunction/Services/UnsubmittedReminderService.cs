using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using My.DAL.Data;
using My.Shared.Constants;
using My.Shared.Dtos.TimeSubmission;
using My.Shared.Rules;

namespace My.Functions.Services;

/// <summary>
/// "Remind an employee about unsubmitted time". Builds the email once so the manager's
/// preview and the sent message are the same, and sends it through the Tyme mailer.
/// </summary>
public sealed class UnsubmittedReminderService
{
    public const string NotSentMessage = "The reminder email did not send.";
    public const string NothingToRemindMessage = "This employee has no unsubmitted months in that period.";
    public const string NoEmailMessage = "This employee has no email address.";

    private readonly ApplicationDbContext _db;
    private readonly SubmissionGridService _grid;
    private readonly TymeSubmitMailer _mailer;
    private readonly ILogger<UnsubmittedReminderService> _logger;

    public UnsubmittedReminderService(
        ApplicationDbContext db,
        SubmissionGridService grid,
        TymeSubmitMailer mailer,
        ILogger<UnsubmittedReminderService> logger)
    {
        _db = db;
        _grid = grid;
        _mailer = mailer;
        _logger = logger;
    }

    public sealed record Prepared(UnsubmittedReminderPreviewDto Preview, string PlainText);

    public async Task<bool> IsEnabledAsync()
    {
        var row = await _db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == Constants.SettingKeys.TymeUnsubmittedReminderEnabled);
        return UnsubmittedReminderRules.IsEnabled(row?.Value);
    }

    public async Task<TymeReminderSettingsDto> LoadSettingsAsync()
    {
        var keys = new[]
        {
            Constants.SettingKeys.TymeReminderCoverage,
            Constants.SettingKeys.TymeReminderMonthsBefore,
            Constants.SettingKeys.TymeReminderMonthsAfter,
            Constants.SettingKeys.TymeReminderStatuses
        };
        var rows = await _db.AppSettings.AsNoTracking()
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value);
        string? Get(string key) => rows.TryGetValue(key, out var v) ? v : null;

        var coverage = UnsubmittedReminderRules.ParseCoverage(Get(Constants.SettingKeys.TymeReminderCoverage));
        return new TymeReminderSettingsDto
        {
            Coverage = coverage,
            MonthsBefore = SubmissionGridRules.ParseOffset(
                Get(Constants.SettingKeys.TymeReminderMonthsBefore), SubmissionGridRules.DefaultMonthsBefore),
            MonthsAfter = SubmissionGridRules.ParseOffset(
                Get(Constants.SettingKeys.TymeReminderMonthsAfter), SubmissionGridRules.DefaultMonthsAfter),
            Statuses = UnsubmittedReminderRules.NormalizeStatuses(
                coverage, UnsubmittedReminderRules.ParseStatuses(Get(Constants.SettingKeys.TymeReminderStatuses)))
        };
    }

    public async Task<TymeReminderSettingsDto> SaveSettingsAsync(TymeReminderSettingsDto dto)
    {
        var coverage = UnsubmittedReminderRules.ParseCoverage(dto.Coverage);
        var statuses = UnsubmittedReminderRules.NormalizeStatuses(coverage, dto.Statuses);
        await UpsertAsync(Constants.SettingKeys.TymeReminderCoverage, coverage,
            "Reminder email grid coverage: month, range or year.");
        await UpsertAsync(Constants.SettingKeys.TymeReminderMonthsBefore,
            dto.MonthsBefore.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Months before the clicked month in the reminder email grid (range coverage).");
        await UpsertAsync(Constants.SettingKeys.TymeReminderMonthsAfter,
            dto.MonthsAfter.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Months after the clicked month in the reminder email grid (range coverage).");
        await UpsertAsync(Constants.SettingKeys.TymeReminderStatuses,
            UnsubmittedReminderRules.StatusesToText(statuses),
            "Status keys shown in the reminder email grid.");
        await _db.SaveChangesAsync();
        return await LoadSettingsAsync();
    }

    private async Task UpsertAsync(string key, string value, string description)
    {
        var row = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row == null)
            _db.AppSettings.Add(new My.DAL.Models.AppSetting { Key = key, Value = value, Description = description });
        else
            row.Value = value;
    }

    /// <summary>
    /// Builds the reminder for one employee from the clicked month and the manager's coverage settings.
    /// Returns an error message when there is nothing to send (not found, no email, no unsubmitted month).
    /// </summary>
    public async Task<(Prepared? Prepared, string? Error)> PrepareAsync(
        string employeeId,
        string managerId,
        int year,
        int month,
        Func<IList<string>, bool> isVisible,
        DateTime utcNow)
    {
        var settings = await LoadSettingsAsync();
        var (from, to) = UnsubmittedReminderRules.Window(
            settings.Coverage, year, month, settings.MonthsBefore, settings.MonthsAfter);
        var grid = await _grid.BuildAsync(from, to, [employeeId], isVisible, utcNow);
        var row = grid.Rows.FirstOrDefault(r => r.UserId == employeeId);
        if (row == null)
            return (null, "Employee not found.");

        // Months whose status the manager chose to show, oldest first (the grid is in month order).
        var shown = settings.Statuses.ToHashSet(StringComparer.Ordinal);
        var keep = row.Cells
            .Select((cell, i) => (cell, month: grid.Months[i]))
            .Where(x => shown.Contains(SubmissionGridRules.StatusKey(x.cell.Status)))
            .ToList();
        // At least one of them must still be unsubmitted, or there is nothing to remind about.
        if (!keep.Any(x => UnsubmittedReminderRules.IsUnsubmitted(x.cell)))
            return (null, NothingToRemindMessage);

        var reminderGrid = new SubmissionGridDto
        {
            Months = keep.Select(x => x.month).ToList(),
            Rows =
            [
                new SubmissionGridRowDto
                {
                    UserId = row.UserId,
                    UserName = row.UserName,
                    Cells = keep.Select(x => x.cell).ToList()
                }
            ]
        };

        var employee = await _db.ApplicationUsers.AsNoTracking()
            .Where(u => u.Id == employeeId)
            .Select(u => new { u.Email })
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(employee?.Email))
            return (null, NoEmailMessage);

        var manager = await _db.ApplicationUsers.AsNoTracking()
            .Where(u => u.Id == managerId)
            .Select(u => new { u.FirstName, u.LastName, u.Email })
            .FirstOrDefaultAsync();
        var managerName = manager == null
            ? "Your Tyme Manager"
            : UserDisplayNameRules.Resolve(manager.FirstName, manager.LastName, manager.Email);

        var baseUrl = Environment.GetEnvironmentVariable("PublicBaseUrl");
        var domain = string.IsNullOrWhiteSpace(baseUrl)
            ? ExpensePublicLinkRules.ProductionBaseUrl
            : baseUrl.Trim().TrimEnd('/');
        var submitUrl = domain + "/tyme/submit";
        string? logoUrl = null;

        var html = UnsubmittedReminderEmailBuilder.Html(row.UserName, managerName, reminderGrid, submitUrl, logoUrl);
        var text = UnsubmittedReminderEmailBuilder.PlainText(row.UserName, managerName, reminderGrid, submitUrl);
        var preview = new UnsubmittedReminderPreviewDto
        {
            ToName = row.UserName,
            ToEmail = employee.Email!,
            Subject = UnsubmittedReminderEmailBuilder.Subject,
            Html = html,
            Months = UnsubmittedReminderEmailBuilder.UnsubmittedMonths(reminderGrid)
        };
        return (new Prepared(preview, text), null);
    }

    /// <summary>Sends a prepared reminder. Returns an error message, or null when sent.</summary>
    public async Task<string?> SendAsync(Prepared prepared)
    {
        try
        {
            var warning = await _mailer.SendAsync(
                [prepared.Preview.ToEmail], prepared.Preview.Subject, prepared.Preview.Html, prepared.PlainText);
            return warning == null ? null : NotSentMessage;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tyme reminder email failed.");
            return NotSentMessage;
        }
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using My.DAL.Data;
using My.DAL.Models;
using My.Shared.Constants;
using My.Shared.Dtos.TimeSubmission;
using My.Shared.Rules;

namespace My.Functions.Services;

/// <summary>
/// Tyme submit notification: stores the Tyme Manager settings (recipients, month window)
/// in AppSettings and emails the submission grid when someone submits.
/// </summary>
public sealed class TymeSubmitNotifier
{
    private readonly ApplicationDbContext _db;
    private readonly SubmissionGridService _grid;
    private readonly TymeSubmitMailer _mailer;
    private readonly ILogger<TymeSubmitNotifier> _logger;

    public TymeSubmitNotifier(
        ApplicationDbContext db,
        SubmissionGridService grid,
        TymeSubmitMailer mailer,
        ILogger<TymeSubmitNotifier> logger)
    {
        _db = db;
        _grid = grid;
        _mailer = mailer;
        _logger = logger;
    }

    public async Task<TymeSubmitNotifySettingsDto> LoadSettingsAsync()
    {
        var keys = new[]
        {
            Constants.SettingKeys.TymeSubmitNotifyUserIds,
            Constants.SettingKeys.TymeSubmitNotifyMonthsBefore,
            Constants.SettingKeys.TymeSubmitNotifyMonthsAfter
        };
        var rows = await _db.AppSettings.AsNoTracking()
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value);
        string? Get(string key) => rows.TryGetValue(key, out var v) ? v : null;

        var recipientIds = SubmissionGridRules.ParseIds(Get(Constants.SettingKeys.TymeSubmitNotifyUserIds));
        var recipients = await ResolveUsersAsync(recipientIds);

        return new TymeSubmitNotifySettingsDto
        {
            Recipients = recipients,
            MonthsBefore = SubmissionGridRules.ParseOffset(
                Get(Constants.SettingKeys.TymeSubmitNotifyMonthsBefore), SubmissionGridRules.DefaultMonthsBefore),
            MonthsAfter = SubmissionGridRules.ParseOffset(
                Get(Constants.SettingKeys.TymeSubmitNotifyMonthsAfter), SubmissionGridRules.DefaultMonthsAfter)
        };
    }

    public async Task<TymeSubmitNotifySettingsDto> SaveSettingsAsync(UpdateTymeSubmitNotifySettingsDto dto)
    {
        var recipientIds = Clean(dto.RecipientIds);
        var knownRecipients = await _db.ApplicationUsers.AsNoTracking()
            .Where(u => recipientIds.Contains(u.Id) && u.IsActive && !u.IsArchived)
            .Select(u => u.Id)
            .ToListAsync();
        var keepRecipients = recipientIds.Where(knownRecipients.Contains).ToList();

        await UpsertAsync(Constants.SettingKeys.TymeSubmitNotifyUserIds,
            JsonSerializer.Serialize(keepRecipients), "User ids emailed the submission grid when time is submitted.");
        await UpsertAsync(Constants.SettingKeys.TymeSubmitNotifyMonthsBefore,
            dto.MonthsBefore.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Months before the current month in the submit email grid.");
        await UpsertAsync(Constants.SettingKeys.TymeSubmitNotifyMonthsAfter,
            dto.MonthsAfter.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Months after the current month in the submit email grid.");
        await _db.SaveChangesAsync();

        return await LoadSettingsAsync();
    }

    /// <summary>
    /// People who can be added as recipients: active, with an email, and Editor or Manager in Tyme.
    /// </summary>
    public async Task<List<TymeSubmitNotifyUserDto>> LoadDirectoryAsync()
    {
        var roles = new[]
        {
            Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Tyme),
            Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Tyme)
        };
        return await (
            from u in _db.ApplicationUsers.AsNoTracking()
            where u.IsActive && !u.IsArchived && u.Email != null && u.Email != ""
                && (from ur in _db.UserRoles
                    join r in _db.Roles on ur.RoleId equals r.Id
                    where ur.UserId == u.Id && r.Name != null && roles.Contains(r.Name)
                    select ur.UserId).Any()
            orderby u.LastName, u.FirstName
            select new TymeSubmitNotifyUserDto
            {
                UserId = u.Id,
                Name = (u.FirstName + " " + u.LastName).Trim(),
                Email = u.Email!
            })
            .ToListAsync();
    }

    /// <summary>
    /// Emails the configured recipients the grid with the submitter's row highlighted.
    /// Best effort. Never throws. Returns a warning string, or null when sent or nothing to send.
    /// </summary>
    public async Task<string?> NotifyAsync(string submittedUserId, int year, int month)
    {
        try
        {
            var settings = await LoadSettingsAsync();
            var to = settings.Recipients.Select(r => r.Email).ToList();
            if (to.Count == 0)
                return null;

            var utcNow = DateTime.UtcNow;
            var (from, toMonth) = SubmissionGridRules.RangeFromOffsets(
                new DateTime(year, month, 1), settings.MonthsBefore, settings.MonthsAfter);

            // Always every active Tyme user.
            var grid = await _grid.BuildAsync(from, toMonth, null, SubmissionGridService.HasTymeRole, utcNow);

            var submitter = await _db.ApplicationUsers.AsNoTracking()
                .Where(u => u.Id == submittedUserId)
                .Select(u => new { u.FirstName, u.LastName, u.Email })
                .FirstOrDefaultAsync();
            var name = submitter == null
                ? "A team member"
                : UserDisplayNameRules.Resolve(submitter.FirstName, submitter.LastName, submitter.Email);

            var baseUrl = Environment.GetEnvironmentVariable("PublicBaseUrl");
            var submitUrl = (string.IsNullOrWhiteSpace(baseUrl)
                ? ExpensePublicLinkRules.ProductionBaseUrl
                : baseUrl.Trim().TrimEnd('/')) + "/tyme/submit";

            var html = SubmissionGridEmailBuilder.Html(name, submittedUserId, year, month, grid, submitUrl);
            var text = SubmissionGridEmailBuilder.PlainText(name, year, month, grid, submitUrl);
            return await _mailer.SendAsync(to, SubmissionGridEmailBuilder.Subject(name), html, text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tyme submit notification failed for user {UserId}.", submittedUserId);
            return TymeSubmitMailer.NotSentMessage;
        }
    }

    private async Task<List<TymeSubmitNotifyUserDto>> ResolveUsersAsync(List<string> ids)
    {
        if (ids.Count == 0) return [];
        var users = await _db.ApplicationUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.IsActive && !u.IsArchived)
            .Select(u => new TymeSubmitNotifyUserDto
            {
                UserId = u.Id,
                Name = (u.FirstName + " " + u.LastName).Trim(),
                Email = u.Email ?? ""
            })
            .ToListAsync();
        return ids.Select(id => users.FirstOrDefault(u => u.UserId == id))
            .Where(u => u != null && !string.IsNullOrWhiteSpace(u.Email))
            .Select(u => u!)
            .ToList();
    }

    private static List<string> Clean(IEnumerable<string>? ids) =>
        (ids ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private async Task UpsertAsync(string key, string value, string description)
    {
        var row = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row == null)
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value, Description = description });
        else
            row.Value = value;
    }
}

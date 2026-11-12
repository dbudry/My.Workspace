using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.Shared.Constants;
using My.Shared.Dtos.TimeSubmission;
using My.Shared.Rules;

namespace My.Functions.Services;

/// <summary>
/// Builds the employees × months submission matrix. One source for the Submit page Grid view
/// and the submit notification email. Lists every active Tyme employee the caller may see,
/// not only people with data, so a person with no time shows as "No time" instead of missing.
/// </summary>
public sealed class SubmissionGridService
{
    private readonly ApplicationDbContext _db;

    public SubmissionGridService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <param name="from">First month, inclusive.</param>
    /// <param name="to">Last month, inclusive. At most <see cref="SubmissionGridRules.MaxMonths"/> months.</param>
    /// <param name="employeeIds">Limit to these users. Null or empty means everyone visible.</param>
    /// <param name="isVisible">True when a user with these roles should be listed.</param>
    public async Task<SubmissionGridDto> BuildAsync(
        (int Year, int Month) from,
        (int Year, int Month) to,
        IReadOnlyCollection<string>? employeeIds,
        Func<IList<string>, bool> isVisible,
        DateTime utcNow)
    {
        var months = SubmissionGridRules.EnumerateMonths(from, to);
        var grid = new SubmissionGridDto
        {
            Months = months.Select(m => new SubmissionGridMonthDto { Year = m.Year, Month = m.Month }).ToList()
        };
        if (months.Count == 0)
            return grid;

        var users = await _db.ApplicationUsers.AsNoTracking()
            .Where(u => u.IsActive && !u.IsArchived)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToListAsync();
        if (employeeIds is { Count: > 0 })
            users = users.Where(u => employeeIds.Contains(u.Id)).ToList();
        if (users.Count == 0)
            return grid;

        var userIds = users.Select(u => u.Id).ToList();
        var roleRows = await (from ur in _db.UserRoles
                              where userIds.Contains(ur.UserId)
                              join r in _db.Roles on ur.RoleId equals r.Id
                              select new { ur.UserId, RoleName = r.Name! })
                             .ToListAsync();
        var rolesByUser = roleRows
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => (IList<string>)g.Select(x => x.RoleName).ToList());

        var listed = users
            .Where(u => isVisible(rolesByUser.TryGetValue(u.Id, out var roles) ? roles : Array.Empty<string>()))
            .ToList();
        if (listed.Count == 0)
            return grid;

        var listedIds = listed.Select(u => u.Id).ToList();
        var rangeStart = new DateTime(months[0].Year, months[0].Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(months[^1].Year, months[^1].Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);

        var taskMonths = await _db.TrackedTasks.AsNoTracking()
            .Where(t => listedIds.Contains(t.UserId)
                && t.StartDate >= rangeStart && t.StartDate < rangeEnd
                && (t.Project == null || t.Project.CountsAsTime))
            .Select(t => new { t.UserId, t.StartDate.Year, t.StartDate.Month })
            .Distinct()
            .ToListAsync();
        var withTime = taskMonths.Select(t => (t.UserId, t.Year, t.Month)).ToHashSet();

        var firstKey = months[0].Year * 12 + months[0].Month;
        var lastKey = months[months.Count - 1].Year * 12 + months[months.Count - 1].Month;
        var submissions = await _db.TimeSubmissions.AsNoTracking()
            .Where(s => listedIds.Contains(s.UserId)
                && (s.Year * 12 + s.Month) >= firstKey
                && (s.Year * 12 + s.Month) <= lastKey)
            .Select(s => new { s.UserId, s.Year, s.Month, s.SubmittedAt, s.TimeSubmissionId })
            .ToListAsync();
        var submissionByKey = submissions.ToDictionary(s => (s.UserId, s.Year, s.Month));

        foreach (var u in listed.OrderBy(u => UserDisplayNameRules.Resolve(u.FirstName, u.LastName, u.Email),
                     StringComparer.OrdinalIgnoreCase))
        {
            var row = new SubmissionGridRowDto
            {
                UserId = u.Id,
                UserName = UserDisplayNameRules.Resolve(u.FirstName, u.LastName, u.Email)
            };
            foreach (var m in months)
            {
                var submitted = submissionByKey.TryGetValue((u.Id, m.Year, m.Month), out var sub);
                row.Cells.Add(new SubmissionGridCellDto
                {
                    Status = SubmissionGridRules.ResolveStatus(
                        submitted, withTime.Contains((u.Id, m.Year, m.Month)), m.Year, m.Month, utcNow),
                    SubmittedAt = submitted ? sub!.SubmittedAt : null,
                    TimeSubmissionId = submitted ? sub!.TimeSubmissionId.ToString() : null
                });
            }
            grid.Rows.Add(row);
        }

        return grid;
    }

    /// <summary>Visible = has an operational role in the Tyme scope (same as the Team view for non-global-admins).</summary>
    public static bool HasTymeRole(IList<string> roles) =>
        Constants.Roles.HasOperationalRoleInScope(roles, Constants.Scopes.Tyme);
}

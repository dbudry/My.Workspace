using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Shared.Constants;
using My.Shared.Dtos.Crm;
using My.Shared.Rules;

namespace My.Functions;

public partial class CrmFunctions
{
    [Function("GetCrmRelationshipTypes")]
    public async Task<IActionResult> GetRelationshipTypesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "crmrelationshiptypes")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireAuthenticated(principal, out _) is { } denied) return denied;

        var types = await RelationshipTypesAsync();
        return new OkObjectResult(types.Select(type => new CrmRelationshipTypeDto
        {
            Name = type.Name,
            Description = type.Description
        }).ToList());
    }

    [Function("SaveCrmRelationshipTypes")]
    public async Task<IActionResult> SaveRelationshipTypesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "crmrelationshiptypes")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Manager) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, saveRelationshipTypesValidator);
        if (error != null) return error;

        var proposed = dto!.Types
            .Select(type => new CrmRelationshipType(type.Name?.Trim() ?? "", type.Description?.Trim() ?? ""))
            .ToList();
        var usageRows = await db.Contacts.AsNoTracking()
            .Where(contact => contact.RelationshipType != null && contact.RelationshipType != "")
            .GroupBy(contact => contact.RelationshipType!)
            .Select(group => new { Name = group.Key, Count = group.Count() })
            .ToListAsync();
        var usage = usageRows.ToDictionary(row => row.Name, row => row.Count, StringComparer.OrdinalIgnoreCase);
        var problem = CrmRelationshipTypeRules.ValidateUpdate(proposed, usage);
        if (problem != null)
            return new BadRequestObjectResult(problem);

        await UpsertSettingAsync(
            Constants.SettingKeys.CrmRelationshipTypes,
            CrmRelationshipTypeRules.Serialize(proposed),
            "JSON array of CRM relationship types (name and description) stored on contacts.");
        await db.SaveChangesAsync();
        return new OkObjectResult(proposed.Select(type => new CrmRelationshipTypeDto
        {
            Name = type.Name,
            Description = type.Description
        }).ToList());
    }

    [Function("GetCrmNotifyUsers")]
    public async Task<IActionResult> GetNotifyUsersAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "crmnotifyusers")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Manager) is { } denied)
            return denied;

        return new OkObjectResult(await NotifyUsersAsync());
    }

    [Function("SaveCrmNotifyUsers")]
    public async Task<IActionResult> SaveNotifyUsersAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "crmnotifyusers")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Manager) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, saveNotifyUsersValidator);
        if (error != null) return error;

        var candidates = (await CrmUsersQuery().Select(user => user.Id).ToListAsync()).ToHashSet(StringComparer.Ordinal);
        var chosen = CrmNotifyRecipientRules.Normalize(dto!.UserIds);
        if (chosen.Any(id => !candidates.Contains(id)))
            return new BadRequestObjectResult("Every recipient must be an active user with a CRM role.");

        await UpsertSettingAsync(
            Constants.SettingKeys.CrmDiscussionNotifyUserIds,
            CrmNotifyRecipientRules.Serialize(chosen),
            "JSON array of user ids who get an email when a CRM discussion is posted.");
        await db.SaveChangesAsync();
        return new OkObjectResult(await NotifyUsersAsync());
    }

    private static readonly string[] CrmUserRoleNames =
    [
        Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Crm),
        Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Crm),
        Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Crm),
        Constants.Roles.Scoped(Constants.Roles.Analyst, Constants.Scopes.Crm)
    ];

    /// <summary>Active, unarchived users who hold a CRM role.</summary>
    private IQueryable<My.DAL.Models.ApplicationUser> CrmUsersQuery() =>
        db.Users.AsNoTracking()
            .Where(user => user.IsActive
                && !user.IsArchived
                && (from userRole in db.UserRoles
                    join role in db.Roles on userRole.RoleId equals role.Id
                    where userRole.UserId == user.Id && role.Name != null && CrmUserRoleNames.Contains(role.Name)
                    select userRole.UserId).Any());

    private async Task<List<CrmNotifyUserDto>> NotifyUsersAsync()
    {
        var json = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == Constants.SettingKeys.CrmDiscussionNotifyUserIds)
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        var selected = CrmNotifyRecipientRules.Parse(json).ToHashSet(StringComparer.Ordinal);
        var users = await CrmUsersQuery()
            .Select(user => new { user.Id, user.FirstName, user.LastName, user.Email })
            .ToListAsync();
        return users
            .Select(user => new CrmNotifyUserDto
            {
                UserId = user.Id,
                Name = UserDisplayNameRules.Resolve(user.FirstName, user.LastName, user.Email),
                Email = user.Email,
                Selected = selected.Contains(user.Id)
            })
            .OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

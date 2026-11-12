using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using My.DAL.Models;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Shared.Constants;
using My.Shared.Dtos.Crm;
using My.Shared.Rules;

namespace My.Functions;

public partial class CrmFunctions
{
    [Function("CreateCrmPotentialStatus")]
    public async Task<IActionResult> CreateStatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "crmpotentials/{id}/statuses")] HttpRequestData req,
        string id)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out var userId, Constants.Roles.Editor) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, createStatusValidator);
        if (error != null) return error;

        var potential = await db.Potentials.AsNoTracking().AnyAsync(row => row.PotentialId == id);
        if (!potential)
            return new NotFoundObjectResult("Potential not found.");

        var stage = await db.CrmStages.AsNoTracking()
            .Where(row => row.CrmStageId == dto!.CrmStageId)
            .Select(row => new { row.CrmStageId, row.Name })
            .FirstOrDefaultAsync();
        if (stage == null)
            return new BadRequestObjectResult("Stage not found.");

        var status = new PotentialStatus
        {
            PotentialStatusId = Guid.NewGuid().ToString(),
            PotentialId = id,
            CrmStageId = stage.CrmStageId,
            StatusDate = dto!.StatusDate.Date,
            Body = dto.Body.Trim(),
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.PotentialStatuses.Add(status);
        await db.SaveChangesAsync();

        var author = await AuthorNameAsync(userId);
        return new OkObjectResult(new PotentialStatusDto
        {
            PotentialStatusId = status.PotentialStatusId,
            PotentialId = status.PotentialId,
            CrmStageId = status.CrmStageId,
            StageName = stage.Name,
            StatusDate = status.StatusDate,
            Body = status.Body,
            CreatedAt = status.CreatedAt,
            CreatedByName = author
        });
    }

    [Function("CreateCrmDiscussion")]
    public async Task<IActionResult> CreateDiscussionAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "crmpotentials/{id}/discussions")] HttpRequestData req,
        string id)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out var userId, Constants.Roles.User) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, createDiscussionValidator);
        if (error != null) return error;

        var potential = await db.Potentials.AsNoTracking()
            .Where(row => row.PotentialId == id)
            .Select(row => new { row.PotentialId, row.Name, row.CrmStageId })
            .FirstOrDefaultAsync();
        if (potential == null)
            return new NotFoundObjectResult("Potential not found.");

        var stageName = await db.CrmStages.AsNoTracking()
            .Where(row => row.CrmStageId == potential.CrmStageId)
            .Select(row => row.Name)
            .FirstOrDefaultAsync() ?? "";

        var note = new CrmDiscussion
        {
            CrmDiscussionId = Guid.NewGuid().ToString(),
            PotentialId = potential.PotentialId,
            CrmStageId = potential.CrmStageId,
            AuthorUserId = userId,
            Message = dto!.Message.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.CrmDiscussions.Add(note);
        await db.SaveChangesAsync();

        var author = await AuthorNameAsync(userId);
        var warning = await NotifyDiscussionAsync(userId, potential.Name, stageName, author, note.Message);
        return new OkObjectResult(new CrmDiscussionDto
        {
            CrmDiscussionId = note.CrmDiscussionId,
            PotentialId = note.PotentialId,
            CrmStageId = note.CrmStageId,
            StageName = stageName,
            Message = note.Message,
            CreatedAt = note.CreatedAt,
            AuthorName = author,
            EmailWarning = warning
        });
    }

    private async Task<string?> NotifyDiscussionAsync(
        string authorUserId,
        string potentialName,
        string stageName,
        string? authorName,
        string message)
    {
        var notifyJson = await db.AppSettings.AsNoTracking()
            .Where(setting => setting.Key == Constants.SettingKeys.CrmDiscussionNotifyUserIds)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync();
        var recipientIds = CrmNotifyRecipientRules.Parse(notifyJson)
            .Where(id => id != authorUserId)
            .ToList();
        if (recipientIds.Count == 0)
            return null;

        var addresses = await CrmUsersQuery()
            .Where(user => recipientIds.Contains(user.Id) && user.Email != null && user.Email != "")
            .Select(user => user.Email!)
            .Distinct()
            .ToListAsync();
        if (addresses.Count == 0)
            return null;

        var who = string.IsNullOrWhiteSpace(authorName) ? "Someone" : authorName;
        return await mailer.SendAsync(
            addresses,
            $"CRM: {potentialName}",
            $"{who} on {stageName}:{Environment.NewLine}{Environment.NewLine}{message}");
    }

    private async Task<string?> AuthorNameAsync(string userId)
    {
        var user = await db.Users.AsNoTracking()
            .Where(row => row.Id == userId)
            .Select(row => new { row.FirstName, row.LastName, row.Email })
            .FirstOrDefaultAsync();
        return user == null ? null : UserDisplayNameRules.Resolve(user.FirstName, user.LastName, user.Email);
    }
}

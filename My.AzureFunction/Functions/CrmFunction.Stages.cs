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
    [Function("GetCrmStages")]
    public async Task<IActionResult> GetStagesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "crmstages")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireCrmRead(principal, out _) is { } denied) return denied;

        var stages = await db.CrmStages.AsNoTracking()
            .OrderBy(stage => stage.SortOrder)
            .ThenBy(stage => stage.Name)
            .Select(stage => new CrmStageDto
            {
                CrmStageId = stage.CrmStageId,
                Name = stage.Name,
                DefaultProbability = stage.DefaultProbability,
                Outcome = stage.Outcome,
                SortOrder = stage.SortOrder
            })
            .ToListAsync();
        return new OkObjectResult(stages);
    }

    [Function("CreateCrmStage")]
    public async Task<IActionResult> CreateStageAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "crmstages")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Manager) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, createStageValidator);
        if (error != null) return error;
        if (!CrmStageRules.TryCanonicalOutcome(dto!.Outcome, out var outcome))
            return new BadRequestObjectResult("Outcome must be Open, Closed Won, or Closed Lost.");
        if (await StageNameTakenAsync(dto.Name, null))
            return new BadRequestObjectResult($"A stage named \"{dto.Name.Trim()}\" already exists.");

        var maxOrder = await db.CrmStages.MaxAsync(stage => (int?)stage.SortOrder) ?? -1;
        var stage = new CrmStage
        {
            CrmStageId = Guid.NewGuid().ToString(),
            Name = dto.Name.Trim(),
            DefaultProbability = dto.DefaultProbability,
            Outcome = outcome,
            SortOrder = maxOrder + 1
        };
        db.CrmStages.Add(stage);
        await db.SaveChangesAsync();
        return new OkObjectResult(ToStageDto(stage));
    }

    [Function("UpdateCrmStage")]
    public async Task<IActionResult> UpdateStageAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "crmstages")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Manager) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, updateStageValidator);
        if (error != null) return error;
        if (!CrmStageRules.TryCanonicalOutcome(dto!.Outcome, out var outcome))
            return new BadRequestObjectResult("Outcome must be Open, Closed Won, or Closed Lost.");

        var stage = await db.CrmStages.FirstOrDefaultAsync(row => row.CrmStageId == dto.CrmStageId);
        if (stage == null)
            return new NotFoundObjectResult("Stage not found.");
        if (await StageNameTakenAsync(dto.Name, stage.CrmStageId))
            return new BadRequestObjectResult($"A stage named \"{dto.Name.Trim()}\" already exists.");

        stage.Name = dto.Name.Trim();
        stage.DefaultProbability = dto.DefaultProbability;
        stage.Outcome = outcome;
        await db.SaveChangesAsync();
        return new OkObjectResult(ToStageDto(stage));
    }

    [Function("DeleteCrmStage")]
    public async Task<IActionResult> DeleteStageAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "crmstages/{id}")] HttpRequestData req,
        string id)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Manager) is { } denied)
            return denied;

        var stage = await db.CrmStages.FirstOrDefaultAsync(row => row.CrmStageId == id);
        if (stage == null)
            return new NotFoundObjectResult("Stage not found.");

        var inUse = await db.Potentials.AnyAsync(row => row.CrmStageId == id)
            || await db.PotentialStatuses.AnyAsync(row => row.CrmStageId == id)
            || await db.CrmDiscussions.AnyAsync(row => row.CrmStageId == id)
            || await db.CrmAttachments.AnyAsync(row => row.CrmStageId == id);
        if (inUse)
            return new ObjectResult("Cannot remove this stage. A potential, status, discussion, or attachment still uses it.")
            {
                StatusCode = 409
            };

        var count = await db.CrmStages.CountAsync();
        if (count <= 1)
            return new ObjectResult("At least one stage is required.") { StatusCode = 409 };

        db.CrmStages.Remove(stage);
        await db.SaveChangesAsync();
        return new NoContentResult();
    }

    [Function("ReorderCrmStages")]
    public async Task<IActionResult> ReorderStagesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "crmstages/reorder")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Manager) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, reorderStagesValidator);
        if (error != null) return error;

        var incoming = dto!.StageIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList();
        var existing = await db.CrmStages.ToListAsync();
        var distinct = incoming.Distinct(StringComparer.Ordinal).Count();
        var coversAll = existing.Count == incoming.Count
            && distinct == incoming.Count
            && existing.All(stage => incoming.Contains(stage.CrmStageId, StringComparer.Ordinal));
        if (!coversAll)
            return new BadRequestObjectResult("Stage order must list every stage once.");

        for (var index = 0; index < incoming.Count; index++)
        {
            var stage = existing.First(row => row.CrmStageId == incoming[index]);
            stage.SortOrder = index;
        }

        await db.SaveChangesAsync();
        return new OkObjectResult(existing
            .OrderBy(stage => stage.SortOrder)
            .Select(ToStageDto)
            .ToList());
    }

    private async Task<bool> StageNameTakenAsync(string name, string? exceptId)
    {
        var trimmed = name.Trim().ToLower();
        return await db.CrmStages.AnyAsync(stage =>
            (exceptId == null || stage.CrmStageId != exceptId)
            && stage.Name.ToLower() == trimmed);
    }

    private static CrmStageDto ToStageDto(CrmStage stage) => new()
    {
        CrmStageId = stage.CrmStageId,
        Name = stage.Name,
        DefaultProbability = stage.DefaultProbability,
        Outcome = stage.Outcome,
        SortOrder = stage.SortOrder
    };
}

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Shared.Dtos.Crm;
using My.Shared.Rules;

namespace My.Functions;

public partial class CrmFunctions
{
    [Function("GetCrmDashboard")]
    public async Task<IActionResult> GetDashboardAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "crmdashboard")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireCrmRead(principal, out _) is { } denied) return denied;

        var rollups = await (
            from stage in db.CrmStages.AsNoTracking()
            join potential in db.Potentials.AsNoTracking().Where(row => !row.IsArchived)
                on stage.CrmStageId equals potential.CrmStageId into potentials
            orderby stage.SortOrder
            select new StageRollup
            {
                CrmStageId = stage.CrmStageId,
                Name = stage.Name,
                Outcome = stage.Outcome,
                SortOrder = stage.SortOrder,
                Count = potentials.Count(),
                Forecast = potentials.Sum(row => (decimal?)row.Amount) ?? 0m,
                Weighted = potentials.Sum(row => (decimal?)((row.Amount ?? 0m) * row.Probability)) ?? 0m
            }).ToListAsync();

        var stages = rollups.Select(row => new CrmDashboardStageDto
        {
            CrmStageId = row.CrmStageId,
            Name = row.Name,
            Outcome = row.Outcome,
            SortOrder = row.SortOrder,
            Count = row.Count,
            ForecastRevenue = row.Forecast,
            ExpectedRevenue = RoundWeighted(row.Weighted)
        }).ToList();

        var openWeighted = rollups
            .Where(row => !CrmStageRules.IsClosed(row.Outcome))
            .Sum(row => row.Weighted);
        return new OkObjectResult(new CrmDashboardDto
        {
            Stages = stages,
            OpenExpectedRevenue = RoundWeighted(openWeighted)
        });
    }

    [Function("GetCrmReport")]
    public async Task<IActionResult> GetReportAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "crmreport")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireCrmRead(principal, out _) is { } denied) return denied;

        var query = HttpListQueryParser.ParseListQuery(req);
        var stageId = BlankToNull(req.Query["stage"]);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (!TryParseDate(req.Query["from"], out var fromDate, out var fromError))
            return new BadRequestObjectResult(fromError);
        if (!TryParseDate(req.Query["to"], out var toDate, out var toError))
            return new BadRequestObjectResult(toError);
        if (fromDate != null && toDate != null && fromDate > toDate)
            return new BadRequestObjectResult("Closing date range is backwards.");

        var rows = db.Potentials.AsNoTracking().Where(potential => !potential.IsArchived);
        if (stageId != null)
            rows = rows.Where(potential => potential.CrmStageId == stageId);
        if (search != null)
        {
            rows = rows.Where(potential =>
                potential.Name.Contains(search)
                || (potential.Description != null && potential.Description.Contains(search))
                || db.Organizations.Any(org =>
                    org.OrganizationId == potential.OrganizationId && org.Name.Contains(search)));
        }

        if (fromDate != null)
            rows = rows.Where(potential => (potential.ActualCloseDate ?? potential.ExpectedCloseDate) >= fromDate);
        if (toDate != null)
            rows = rows.Where(potential => (potential.ActualCloseDate ?? potential.ExpectedCloseDate) <= toDate);

        var total = await rows.CountAsync();
        var pageSize = query.EffectivePageSize;
        var page = query.PageNumber < 1 ? 1 : query.PageNumber;
        var items = await rows
            .OrderBy(potential => (potential.ActualCloseDate ?? potential.ExpectedCloseDate) == null)
            .ThenBy(potential => potential.ActualCloseDate ?? potential.ExpectedCloseDate)
            .ThenBy(potential => potential.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(potential => new CrmReportRowDto
            {
                PotentialId = potential.PotentialId,
                AccountName = db.Organizations
                    .Where(org => org.OrganizationId == potential.OrganizationId)
                    .Select(org => org.Name)
                    .FirstOrDefault(),
                PotentialName = potential.Name,
                Description = potential.Description,
                StageName = db.CrmStages
                    .Where(stage => stage.CrmStageId == potential.CrmStageId)
                    .Select(stage => stage.Name)
                    .FirstOrDefault() ?? "",
                Probability = potential.Probability,
                Amount = potential.Amount,
                ClosingDate = potential.ActualCloseDate ?? potential.ExpectedCloseDate,
                Status = db.PotentialStatuses
                    .Where(status => status.PotentialId == potential.PotentialId)
                    .OrderByDescending(status => status.StatusDate)
                    .ThenByDescending(status => status.CreatedAt)
                    .Select(status => status.Body)
                    .FirstOrDefault()
            })
            .ToListAsync();
        return Page(items, total, pageSize, page);
    }

    private static decimal RoundWeighted(decimal weighted) =>
        Math.Round(weighted / 100m, 2, MidpointRounding.AwayFromZero);

    private static bool TryParseDate(string? value, out DateTime? date, out string? error)
    {
        date = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (!DateTime.TryParse(value, out var parsed))
        {
            error = "Closing date is not a date.";
            return false;
        }

        date = parsed.Date;
        return true;
    }

    private sealed class StageRollup
    {
        public string CrmStageId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Outcome { get; set; } = "";
        public int SortOrder { get; set; }
        public int Count { get; set; }
        public decimal Forecast { get; set; }
        public decimal Weighted { get; set; }
    }
}

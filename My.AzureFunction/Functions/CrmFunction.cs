using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.DAL.Models;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Shared.Constants;
using My.Shared.Dtos.Contact;
using My.Shared.Dtos.Crm;
using My.Shared.Dtos.Paging;
using My.Shared.Rules;
using My.Shared.Validation;
using System.Security.Claims;

namespace My.Functions;

public class CrmFunctions
{
    private readonly ApplicationDbContext db;
    private readonly IValidator<CreateOpportunityDto> createOpportunityValidator;
    private readonly IValidator<UpdateOpportunityDto> updateOpportunityValidator;
    private readonly IValidator<SetOpportunityArchivedDto> archiveValidator;
    private readonly IValidator<CreateCrmActivityDto> createActivityValidator;
    private readonly IValidator<UpdateCrmActivityDto> updateActivityValidator;
    private readonly IValidator<CreateContactDto> createContactValidator;

    public CrmFunctions(
        ApplicationDbContext db,
        IValidator<CreateOpportunityDto> createOpportunityValidator,
        IValidator<UpdateOpportunityDto> updateOpportunityValidator,
        IValidator<SetOpportunityArchivedDto> archiveValidator,
        IValidator<CreateCrmActivityDto> createActivityValidator,
        IValidator<UpdateCrmActivityDto> updateActivityValidator,
        IValidator<CreateContactDto> createContactValidator)
    {
        this.db = db;
        this.createOpportunityValidator = createOpportunityValidator;
        this.updateOpportunityValidator = updateOpportunityValidator;
        this.archiveValidator = archiveValidator;
        this.createActivityValidator = createActivityValidator;
        this.updateActivityValidator = updateActivityValidator;
        this.createContactValidator = createContactValidator;
    }

    [Function("GetCrmOpportunities")]
    public async Task<IActionResult> GetOpportunitiesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "crmopportunities")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _) is { } denied) return denied;

        var query = HttpListQueryParser.ParseListQuery(req);
        var stage = req.Query["stage"];
        var organizationId = req.Query["organizationId"];
        var canonicalStage = CrmStageRules.TryCanonical(stage, out var parsedStage) ? parsedStage : null;
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        var rows = db.Opportunities.AsNoTracking().Where(o => query.IncludeArchived || !o.IsArchived);
        if (canonicalStage != null)
            rows = rows.Where(o => o.Stage == canonicalStage);
        if (!string.IsNullOrWhiteSpace(organizationId))
            rows = rows.Where(o => o.OrganizationId == organizationId);
        if (search != null)
            rows = rows.Where(o => o.Name.Contains(search));

        var total = await rows.CountAsync();
        var pageSize = query.EffectivePageSize;
        var page = query.PageNumber < 1 ? 1 : query.PageNumber;

        var items = await ProjectList(rows)
            .OrderBy(o => o.ExpectedCloseDate == null)
            .ThenBy(o => o.ExpectedCloseDate)
            .ThenBy(o => o.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return new OkObjectResult(new PagedResponse<OpportunityListItemDto>
        {
            Items = items,
            TotalCount = total,
            PageSize = pageSize,
            CurrentPage = page,
            TotalPages = totalPages,
            HasNext = page < totalPages,
            HasPrevious = page > 1
        });
    }

    [Function("GetCrmOpportunity")]
    public async Task<IActionResult> GetOpportunityAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "crmopportunities/{id}")] HttpRequestData req,
        string id)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _) is { } denied) return denied;

        var item = await ProjectDetail(db.Opportunities.AsNoTracking().Where(o => o.OpportunityId == id))
            .FirstOrDefaultAsync();
        if (item == null)
            return new NotFoundObjectResult("Opportunity not found.");

        item.Activities = await db.CrmActivities.AsNoTracking()
            .Where(a => a.OpportunityId == id)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new CrmActivityDto
            {
                CrmActivityId = a.CrmActivityId,
                OpportunityId = a.OpportunityId,
                ActivityType = a.ActivityType,
                Subject = a.Subject,
                Body = a.Body,
                DueAt = a.DueAt,
                CompletedAt = a.CompletedAt,
                OwnerUserId = a.OwnerUserId,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return new OkObjectResult(item);
    }

    [Function("CreateCrmOpportunity")]
    public async Task<IActionResult> CreateOpportunityAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "crmopportunities")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out var userId, Constants.Roles.Editor) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, createOpportunityValidator);
        if (error != null) return error;

        var linked = await ResolveLinksAsync(dto!);
        if (linked.Error != null) return linked.Error;

        var now = DateTimeOffset.UtcNow;
        var opportunity = new Opportunity
        {
            OpportunityId = Guid.NewGuid().ToString(),
            Name = dto!.Name.Trim(),
            Stage = linked.Stage,
            Amount = dto.Amount,
            ExpectedCloseDate = AsDate(dto.ExpectedCloseDate),
            OwnerUserId = userId,
            OrganizationId = linked.OrganizationId,
            ContactId = linked.ContactId,
            Note = BlankToNull(dto.Note),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Opportunities.Add(opportunity);
        await db.SaveChangesAsync();

        var created = await ProjectDetail(db.Opportunities.AsNoTracking()
                .Where(o => o.OpportunityId == opportunity.OpportunityId))
            .FirstAsync();
        created.Activities = new List<CrmActivityDto>();
        return new OkObjectResult(created);
    }

    [Function("UpdateCrmOpportunity")]
    public async Task<IActionResult> UpdateOpportunityAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "crmopportunities")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Editor) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, updateOpportunityValidator);
        if (error != null) return error;

        var opportunity = await db.Opportunities.FirstOrDefaultAsync(o => o.OpportunityId == dto!.OpportunityId);
        if (opportunity == null)
            return new NotFoundObjectResult("Opportunity not found.");

        var linked = await ResolveLinksAsync(dto!);
        if (linked.Error != null) return linked.Error;

        opportunity.Name = dto!.Name.Trim();
        opportunity.Stage = linked.Stage;
        opportunity.Amount = dto.Amount;
        opportunity.ExpectedCloseDate = AsDate(dto.ExpectedCloseDate);
        opportunity.OrganizationId = linked.OrganizationId;
        opportunity.ContactId = linked.ContactId;
        opportunity.Note = BlankToNull(dto.Note);
        opportunity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var updated = await ProjectDetail(db.Opportunities.AsNoTracking()
                .Where(o => o.OpportunityId == opportunity.OpportunityId))
            .FirstAsync();
        return new OkObjectResult(updated);
    }

    [Function("ArchiveCrmOpportunity")]
    public async Task<IActionResult> ArchiveOpportunityAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "crmopportunities/{id}/archive")] HttpRequestData req,
        string id)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Manager) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, archiveValidator);
        if (error != null) return error;

        var opportunity = await db.Opportunities.FirstOrDefaultAsync(o => o.OpportunityId == id);
        if (opportunity == null)
            return new NotFoundObjectResult("Opportunity not found.");

        opportunity.IsArchived = dto!.IsArchived;
        opportunity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return new OkObjectResult(new { opportunity.OpportunityId, opportunity.IsArchived });
    }

    [Function("DeleteCrmOpportunity")]
    public async Task<IActionResult> DeleteOpportunityAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "crmopportunities/{id}")] HttpRequestData req,
        string id)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Manager) is { } denied)
            return denied;

        var opportunity = await db.Opportunities.FirstOrDefaultAsync(o => o.OpportunityId == id);
        if (opportunity == null)
            return new NotFoundObjectResult("Opportunity not found.");

        db.Opportunities.Remove(opportunity);
        await db.SaveChangesAsync();
        return new NoContentResult();
    }

    [Function("CreateCrmContact")]
    public async Task<IActionResult> CreateContactAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "crmcontacts")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Editor) is { } denied)
            return denied;

        var contactTypesJson = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == Constants.SettingKeys.ContactTypes)
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        var allowedTypes = ContactTypeRules.Parse(contactTypesJson);
        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(
            req,
            createContactValidator,
            ctx =>
            {
                ctx.RootContextData[ValidationContextKeys.AllowedContactTypes] = allowedTypes;
                ctx.RootContextData[ValidationContextKeys.ContactTypeRequired] = false;
            });
        if (error != null) return error;

        if (string.IsNullOrWhiteSpace(dto!.OrganizationId))
            return new BadRequestObjectResult("Organization is required.");

        var orgExists = await db.Organizations.AnyAsync(o => o.OrganizationId == dto.OrganizationId);
        if (!orgExists)
            return new BadRequestObjectResult("Organization not found.");

        var requestedType = string.IsNullOrWhiteSpace(dto.ContactType)
            ? ContactTypeRules.DefaultForManualEntry(allowedTypes)
            : dto.ContactType;
        var contact = new Contact
        {
            ContactId = Guid.NewGuid().ToString(),
            Name = dto.Name.Trim(),
            Title = BlankToNull(dto.Title),
            PhoneNumber = BlankToNull(dto.PhoneNumber),
            Email = BlankToNull(dto.Email),
            ContactType = ContactTypeRules.Normalize(requestedType, allowedTypes),
            OrganizationId = dto.OrganizationId
        };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        return new OkObjectResult(new ContactDto
        {
            ContactId = contact.ContactId,
            Name = contact.Name,
            Title = contact.Title,
            PhoneNumber = contact.PhoneNumber,
            Email = contact.Email,
            ContactType = contact.ContactType,
            OrganizationId = contact.OrganizationId
        });
    }

    [Function("CreateCrmActivity")]
    public async Task<IActionResult> CreateActivityAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "crmactivities")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out var userId, Constants.Roles.Editor) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, createActivityValidator);
        if (error != null) return error;

        var exists = await db.Opportunities.AnyAsync(o => o.OpportunityId == dto!.OpportunityId);
        if (!exists)
            return new NotFoundObjectResult("Opportunity not found.");

        CrmActivityTypeRules.TryCanonical(dto!.ActivityType, out var activityType);
        var activity = new CrmActivity
        {
            CrmActivityId = Guid.NewGuid().ToString(),
            OpportunityId = dto.OpportunityId,
            ActivityType = activityType,
            Subject = dto.Subject.Trim(),
            Body = BlankToNull(dto.Body),
            DueAt = dto.DueAt,
            OwnerUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.CrmActivities.Add(activity);
        await db.SaveChangesAsync();
        return new OkObjectResult(ToDto(activity));
    }

    [Function("UpdateCrmActivity")]
    public async Task<IActionResult> UpdateActivityAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "crmactivities")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Editor) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, updateActivityValidator);
        if (error != null) return error;

        var activity = await db.CrmActivities.FirstOrDefaultAsync(a => a.CrmActivityId == dto!.CrmActivityId);
        if (activity == null)
            return new NotFoundObjectResult("Activity not found.");

        CrmActivityTypeRules.TryCanonical(dto!.ActivityType, out var activityType);
        activity.ActivityType = activityType;
        activity.Subject = dto.Subject.Trim();
        activity.Body = BlankToNull(dto.Body);
        activity.DueAt = dto.DueAt;
        activity.CompletedAt = dto.CompletedAt;
        await db.SaveChangesAsync();
        return new OkObjectResult(ToDto(activity));
    }

    [Function("DeleteCrmActivity")]
    public async Task<IActionResult> DeleteActivityAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "crmactivities/{id}")] HttpRequestData req,
        string id)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Manager) is { } denied)
            return denied;

        var activity = await db.CrmActivities.FirstOrDefaultAsync(a => a.CrmActivityId == id);
        if (activity == null)
            return new NotFoundObjectResult("Activity not found.");

        db.CrmActivities.Remove(activity);
        await db.SaveChangesAsync();
        return new NoContentResult();
    }

    private async Task<(string Stage, string? OrganizationId, string? ContactId, IActionResult? Error)> ResolveLinksAsync(
        CreateOpportunityDto dto)
    {
        CrmStageRules.TryCanonical(dto.Stage, out var stage);
        var organizationId = BlankToNull(dto.OrganizationId);
        var contactId = BlankToNull(dto.ContactId);

        if (organizationId != null)
        {
            var orgExists = await db.Organizations.AnyAsync(o => o.OrganizationId == organizationId);
            if (!orgExists)
                return (stage, null, null, new BadRequestObjectResult("Organization not found."));
        }

        if (contactId != null)
        {
            var contact = await db.Contacts.AsNoTracking()
                .Where(c => c.ContactId == contactId)
                .Select(c => new { c.OrganizationId })
                .FirstOrDefaultAsync();
            if (contact == null)
                return (stage, null, null, new BadRequestObjectResult("Contact not found."));

            if (organizationId != null
                && contact.OrganizationId != null
                && contact.OrganizationId != organizationId)
            {
                return (stage, null, null, new BadRequestObjectResult("Contact is not in that organization."));
            }

            organizationId ??= contact.OrganizationId;
        }

        return (stage, organizationId, contactId, null);
    }

    private IQueryable<OpportunityListItemDto> ProjectList(IQueryable<Opportunity> rows) =>
        from o in rows
        join org in db.Organizations.AsNoTracking() on o.OrganizationId equals org.OrganizationId into orgs
        from org in orgs.DefaultIfEmpty()
        join contact in db.Contacts.AsNoTracking() on o.ContactId equals contact.ContactId into contacts
        from contact in contacts.DefaultIfEmpty()
        select new OpportunityListItemDto
        {
            OpportunityId = o.OpportunityId,
            Name = o.Name,
            Stage = o.Stage,
            Amount = o.Amount,
            ExpectedCloseDate = o.ExpectedCloseDate,
            OrganizationId = o.OrganizationId,
            OrganizationName = org != null ? org.Name : null,
            ContactId = o.ContactId,
            ContactName = contact != null ? contact.Name : null,
            IsArchived = o.IsArchived
        };

    private IQueryable<OpportunityDetailDto> ProjectDetail(IQueryable<Opportunity> rows) =>
        from o in rows
        join org in db.Organizations.AsNoTracking() on o.OrganizationId equals org.OrganizationId into orgs
        from org in orgs.DefaultIfEmpty()
        join contact in db.Contacts.AsNoTracking() on o.ContactId equals contact.ContactId into contacts
        from contact in contacts.DefaultIfEmpty()
        select new OpportunityDetailDto
        {
            OpportunityId = o.OpportunityId,
            Name = o.Name,
            Stage = o.Stage,
            Amount = o.Amount,
            ExpectedCloseDate = o.ExpectedCloseDate,
            OrganizationId = o.OrganizationId,
            OrganizationName = org != null ? org.Name : null,
            ContactId = o.ContactId,
            ContactName = contact != null ? contact.Name : null,
            IsArchived = o.IsArchived,
            Note = o.Note,
            OwnerUserId = o.OwnerUserId
        };

    private static CrmActivityDto ToDto(CrmActivity activity) => new()
    {
        CrmActivityId = activity.CrmActivityId,
        OpportunityId = activity.OpportunityId,
        ActivityType = activity.ActivityType,
        Subject = activity.Subject,
        Body = activity.Body,
        DueAt = activity.DueAt,
        CompletedAt = activity.CompletedAt,
        OwnerUserId = activity.OwnerUserId,
        CreatedAt = activity.CreatedAt
    };

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? AsDate(DateTime? value) =>
        value.HasValue ? value.Value.Date : null;
}

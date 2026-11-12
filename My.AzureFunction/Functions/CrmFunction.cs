using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.DAL.Models;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Functions.Services;
using My.Shared.Constants;
using My.Shared.Dtos.Contact;
using My.Shared.Dtos.Crm;
using My.Shared.Dtos.Paging;
using My.Shared.Rules;
using My.Shared.Validation;

namespace My.Functions;

public partial class CrmFunctions
{
    private readonly ApplicationDbContext db;
    private readonly GoogleDriveService drive;
    private readonly CrmDiscussionMailer mailer;
    private readonly IValidator<CreatePotentialDto> createPotentialValidator;
    private readonly IValidator<UpdatePotentialDto> updatePotentialValidator;
    private readonly IValidator<SetPotentialArchivedDto> archiveValidator;
    private readonly IValidator<CreatePotentialStatusDto> createStatusValidator;
    private readonly IValidator<CreateCrmDiscussionDto> createDiscussionValidator;
    private readonly IValidator<CreateCrmStageDto> createStageValidator;
    private readonly IValidator<UpdateCrmStageDto> updateStageValidator;
    private readonly IValidator<ReorderCrmStagesDto> reorderStagesValidator;
    private readonly IValidator<SaveCrmRelationshipTypesDto> saveRelationshipTypesValidator;
    private readonly IValidator<SaveCrmNotifyUsersDto> saveNotifyUsersValidator;
    private readonly IValidator<UploadCrmAttachmentDto> uploadAttachmentValidator;
    private readonly IValidator<MoveCrmAttachmentStageDto> moveAttachmentValidator;
    private readonly IValidator<CreateContactDto> createContactValidator;

    public CrmFunctions(
        ApplicationDbContext db,
        GoogleDriveService drive,
        CrmDiscussionMailer mailer,
        IValidator<CreatePotentialDto> createPotentialValidator,
        IValidator<UpdatePotentialDto> updatePotentialValidator,
        IValidator<SetPotentialArchivedDto> archiveValidator,
        IValidator<CreatePotentialStatusDto> createStatusValidator,
        IValidator<CreateCrmDiscussionDto> createDiscussionValidator,
        IValidator<CreateCrmStageDto> createStageValidator,
        IValidator<UpdateCrmStageDto> updateStageValidator,
        IValidator<ReorderCrmStagesDto> reorderStagesValidator,
        IValidator<SaveCrmRelationshipTypesDto> saveRelationshipTypesValidator,
        IValidator<SaveCrmNotifyUsersDto> saveNotifyUsersValidator,
        IValidator<UploadCrmAttachmentDto> uploadAttachmentValidator,
        IValidator<MoveCrmAttachmentStageDto> moveAttachmentValidator,
        IValidator<CreateContactDto> createContactValidator)
    {
        this.db = db;
        this.drive = drive;
        this.mailer = mailer;
        this.createPotentialValidator = createPotentialValidator;
        this.updatePotentialValidator = updatePotentialValidator;
        this.archiveValidator = archiveValidator;
        this.createStatusValidator = createStatusValidator;
        this.createDiscussionValidator = createDiscussionValidator;
        this.createStageValidator = createStageValidator;
        this.updateStageValidator = updateStageValidator;
        this.reorderStagesValidator = reorderStagesValidator;
        this.saveRelationshipTypesValidator = saveRelationshipTypesValidator;
        this.saveNotifyUsersValidator = saveNotifyUsersValidator;
        this.uploadAttachmentValidator = uploadAttachmentValidator;
        this.moveAttachmentValidator = moveAttachmentValidator;
        this.createContactValidator = createContactValidator;
    }

    [Function("GetCrmPotentials")]
    public async Task<IActionResult> GetPotentialsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "crmpotentials")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireCrmRead(principal, out _) is { } denied) return denied;

        var query = HttpListQueryParser.ParseListQuery(req);
        var stageId = BlankToNull(req.Query["stage"]);
        var organizationId = BlankToNull(req.Query["organizationId"]);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        var rows = db.Potentials.AsNoTracking().Where(p => query.IncludeArchived || !p.IsArchived);
        if (stageId != null)
            rows = rows.Where(p => p.CrmStageId == stageId);
        if (organizationId != null)
            rows = rows.Where(p => p.OrganizationId == organizationId);
        if (search != null)
            rows = rows.Where(p => p.Name.Contains(search));

        var total = await rows.CountAsync();
        var pageSize = query.EffectivePageSize;
        var page = query.PageNumber < 1 ? 1 : query.PageNumber;
        var items = await ProjectList(rows)
            .OrderBy(p => p.ExpectedCloseDate == null)
            .ThenBy(p => p.ExpectedCloseDate)
            .ThenBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return Page(items, total, pageSize, page);
    }

    [Function("GetCrmPotential")]
    public async Task<IActionResult> GetPotentialAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "crmpotentials/{id}")] HttpRequestData req,
        string id)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireCrmRead(principal, out _) is { } denied) return denied;

        var row = await ProjectDetail(db.Potentials.AsNoTracking().Where(p => p.PotentialId == id))
            .FirstOrDefaultAsync();
        if (row == null)
            return new NotFoundObjectResult("Potential not found.");

        var item = FinishDetail(row);
        item.Statuses = await StatusesAsync(id);
        item.Discussions = await DiscussionsAsync(id);
        item.Attachments = await AttachmentsAsync(id);
        return new OkObjectResult(item);
    }

    [Function("CreateCrmPotential")]
    public async Task<IActionResult> CreatePotentialAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "crmpotentials")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out var userId, Constants.Roles.Editor) is { } denied)
            return denied;

        var relationshipTypes = await RelationshipTypesAsync();
        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(
            req,
            createPotentialValidator,
            ctx => ctx.RootContextData[ValidationContextKeys.AllowedRelationshipTypes] = relationshipTypes);
        if (error != null) return error;

        var linked = await ResolveLinksAsync(dto!);
        if (linked.Error != null) return linked.Error;

        var now = DateTimeOffset.UtcNow;
        var potential = new Potential
        {
            PotentialId = Guid.NewGuid().ToString(),
            Name = dto!.Name.Trim(),
            Description = BlankToNull(dto.Description),
            CrmStageId = linked.Stage!.CrmStageId,
            Probability = dto.Probability,
            Amount = dto.Amount,
            ExpectedCloseDate = AsDate(dto.ExpectedCloseDate),
            ActualCloseDate = CrmStageRules.ResolveActualClose(true, linked.Stage.Outcome, AsDate(dto.ActualCloseDate), DateTime.UtcNow),
            OwnerUserId = userId,
            CreatedByUserId = userId,
            ModifiedByUserId = userId,
            OrganizationId = linked.OrganizationId,
            ContactId = linked.ContactId,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Potentials.Add(potential);
        await ApplyRelationshipAsync(linked.ContactId, dto.RelationshipType, relationshipTypes);
        await db.SaveChangesAsync();
        return new OkObjectResult(await LoadDetailAsync(potential.PotentialId));
    }

    [Function("UpdateCrmPotential")]
    public async Task<IActionResult> UpdatePotentialAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "crmpotentials")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out var userId, Constants.Roles.Editor) is { } denied)
            return denied;

        var relationshipTypes = await RelationshipTypesAsync();
        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(
            req,
            updatePotentialValidator,
            ctx => ctx.RootContextData[ValidationContextKeys.AllowedRelationshipTypes] = relationshipTypes);
        if (error != null) return error;

        var potential = await db.Potentials.FirstOrDefaultAsync(p => p.PotentialId == dto!.PotentialId);
        if (potential == null)
            return new NotFoundObjectResult("Potential not found.");

        var linked = await ResolveLinksAsync(dto!);
        if (linked.Error != null) return linked.Error;

        var stageChanged = !string.Equals(potential.CrmStageId, linked.Stage!.CrmStageId, StringComparison.Ordinal);
        potential.Name = dto!.Name.Trim();
        potential.Description = BlankToNull(dto.Description);
        potential.CrmStageId = linked.Stage.CrmStageId;
        potential.Probability = dto.Probability;
        potential.Amount = dto.Amount;
        potential.ExpectedCloseDate = AsDate(dto.ExpectedCloseDate);
        potential.ActualCloseDate = CrmStageRules.ResolveActualClose(
            stageChanged, linked.Stage.Outcome, AsDate(dto.ActualCloseDate), DateTime.UtcNow);
        potential.OrganizationId = linked.OrganizationId;
        potential.ContactId = linked.ContactId;
        potential.ModifiedByUserId = userId;
        potential.UpdatedAt = DateTimeOffset.UtcNow;
        await ApplyRelationshipAsync(linked.ContactId, dto.RelationshipType, relationshipTypes);
        await db.SaveChangesAsync();
        return new OkObjectResult(await LoadDetailAsync(potential.PotentialId));
    }

    [Function("ArchiveCrmPotential")]
    public async Task<IActionResult> ArchivePotentialAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "crmpotentials/{id}/archive")] HttpRequestData req,
        string id)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out var userId, Constants.Roles.Manager) is { } denied)
            return denied;

        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(req, archiveValidator);
        if (error != null) return error;

        var potential = await db.Potentials.FirstOrDefaultAsync(p => p.PotentialId == id);
        if (potential == null)
            return new NotFoundObjectResult("Potential not found.");

        potential.IsArchived = dto!.IsArchived;
        potential.ModifiedByUserId = userId;
        potential.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return new OkObjectResult(new { potential.PotentialId, potential.IsArchived });
    }

    [Function("DeleteCrmPotential")]
    public async Task<IActionResult> DeletePotentialAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "crmpotentials/{id}")] HttpRequestData req,
        string id)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireScopedCrm(principal, out _, Constants.Roles.Manager) is { } denied)
            return denied;

        var potential = await db.Potentials.FirstOrDefaultAsync(p => p.PotentialId == id);
        if (potential == null)
            return new NotFoundObjectResult("Potential not found.");

        var fileIds = await db.CrmAttachments.AsNoTracking()
            .Where(a => a.PotentialId == id && a.DriveFileId != "")
            .Select(a => a.DriveFileId)
            .ToListAsync();
        if (fileIds.Count > 0)
        {
            var access = await TryGetCrmDriveAsync();
            if (access.Error != null) return access.Error;
            foreach (var fileId in fileIds)
            {
                var deleted = await TryDeleteDriveFileAsync(access.Token!, fileId);
                if (deleted != null) return deleted;
            }
        }

        db.Potentials.Remove(potential);
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
        var relationshipTypes = await RelationshipTypesAsync();
        var (dto, error) = await RequestValidator.ReadJsonAndValidateAsync(
            req,
            createContactValidator,
            ctx =>
            {
                ctx.RootContextData[ValidationContextKeys.AllowedContactTypes] = allowedTypes;
                ctx.RootContextData[ValidationContextKeys.AllowedRelationshipTypes] = relationshipTypes;
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
        var requestedRelationship = string.IsNullOrWhiteSpace(dto.RelationshipType)
            ? CrmRelationshipTypeRules.DecisionMaker
            : dto.RelationshipType;
        var contact = new Contact
        {
            ContactId = Guid.NewGuid().ToString(),
            Name = dto.Name.Trim(),
            Title = BlankToNull(dto.Title),
            PhoneNumber = BlankToNull(dto.PhoneNumber),
            Email = BlankToNull(dto.Email),
            ContactType = ContactTypeRules.Normalize(requestedType, allowedTypes),
            RelationshipType = CrmRelationshipTypeRules.Canonical(requestedRelationship, relationshipTypes)
                ?? relationshipTypes.FirstOrDefault()?.Name,
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
            RelationshipType = contact.RelationshipType,
            OrganizationId = contact.OrganizationId
        });
    }

    private async Task<PotentialDetailDto> LoadDetailAsync(string potentialId) =>
        FinishDetail(await ProjectDetail(db.Potentials.AsNoTracking().Where(p => p.PotentialId == potentialId)).FirstAsync());

    private async Task<(CrmStage? Stage, string? OrganizationId, string? ContactId, IActionResult? Error)> ResolveLinksAsync(
        CreatePotentialDto dto)
    {
        var stage = await db.CrmStages.AsNoTracking().FirstOrDefaultAsync(s => s.CrmStageId == dto.CrmStageId);
        if (stage == null)
            return (null, null, null, new BadRequestObjectResult("Stage not found."));

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
            if (organizationId != null && contact.OrganizationId != null && contact.OrganizationId != organizationId)
                return (stage, null, null, new BadRequestObjectResult("Contact is not in that organization."));
            organizationId ??= contact.OrganizationId;
        }

        if (organizationId != null && contactId == null)
            return (stage, organizationId, null, new BadRequestObjectResult("Contact is required."));

        return (stage, organizationId, contactId, null);
    }

    private async Task ApplyRelationshipAsync(
        string? contactId,
        string? relationshipType,
        IReadOnlyList<CrmRelationshipType> allowed)
    {
        if (contactId == null || relationshipType == null)
            return;
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.ContactId == contactId);
        if (contact == null)
            return;
        contact.RelationshipType = CrmRelationshipTypeRules.Canonical(relationshipType, allowed);
    }

    private IQueryable<PotentialListItemDto> ProjectList(IQueryable<Potential> rows) =>
        from p in rows
        join stage in db.CrmStages.AsNoTracking() on p.CrmStageId equals stage.CrmStageId
        join org in db.Organizations.AsNoTracking() on p.OrganizationId equals org.OrganizationId into orgs
        from org in orgs.DefaultIfEmpty()
        join contact in db.Contacts.AsNoTracking() on p.ContactId equals contact.ContactId into contacts
        from contact in contacts.DefaultIfEmpty()
        select new PotentialListItemDto
        {
            PotentialId = p.PotentialId,
            Name = p.Name,
            OrganizationId = p.OrganizationId,
            OrganizationName = org != null ? org.Name : null,
            ContactId = p.ContactId,
            ContactName = contact != null ? contact.Name : null,
            CrmStageId = p.CrmStageId,
            StageName = stage.Name,
            StageOutcome = stage.Outcome,
            Probability = p.Probability,
            Amount = p.Amount,
            ExpectedCloseDate = p.ExpectedCloseDate,
            IsArchived = p.IsArchived
        };

    private IQueryable<DetailNameRow> ProjectDetail(IQueryable<Potential> rows) =>
        from p in rows
        join stage in db.CrmStages.AsNoTracking() on p.CrmStageId equals stage.CrmStageId
        join org in db.Organizations.AsNoTracking() on p.OrganizationId equals org.OrganizationId into orgs
        from org in orgs.DefaultIfEmpty()
        join contact in db.Contacts.AsNoTracking() on p.ContactId equals contact.ContactId into contacts
        from contact in contacts.DefaultIfEmpty()
        join created in db.Users.AsNoTracking() on p.CreatedByUserId equals created.Id into createdUsers
        from created in createdUsers.DefaultIfEmpty()
        join modified in db.Users.AsNoTracking() on p.ModifiedByUserId equals modified.Id into modifiedUsers
        from modified in modifiedUsers.DefaultIfEmpty()
        select new DetailNameRow
        {
            Item = new PotentialDetailDto
            {
                PotentialId = p.PotentialId,
                Name = p.Name,
                Description = p.Description,
                OrganizationId = p.OrganizationId,
                OrganizationName = org != null ? org.Name : null,
                ContactId = p.ContactId,
                ContactName = contact != null ? contact.Name : null,
                RelationshipType = contact != null ? contact.RelationshipType : null,
                CrmStageId = p.CrmStageId,
                StageName = stage.Name,
                StageOutcome = stage.Outcome,
                Probability = p.Probability,
                Amount = p.Amount,
                ExpectedCloseDate = p.ExpectedCloseDate,
                ActualCloseDate = p.ActualCloseDate,
                IsArchived = p.IsArchived,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            },
            CreatedPresent = created != null,
            CreatedFirst = created != null ? created.FirstName : null,
            CreatedLast = created != null ? created.LastName : null,
            CreatedEmail = created != null ? created.Email : null,
            ModifiedPresent = modified != null,
            ModifiedFirst = modified != null ? modified.FirstName : null,
            ModifiedLast = modified != null ? modified.LastName : null,
            ModifiedEmail = modified != null ? modified.Email : null
        };

    private static PotentialDetailDto FinishDetail(DetailNameRow row)
    {
        row.Item.CreatedByName = DisplayName(row.CreatedPresent, row.CreatedFirst, row.CreatedLast, row.CreatedEmail);
        row.Item.ModifiedByName = DisplayName(row.ModifiedPresent, row.ModifiedFirst, row.ModifiedLast, row.ModifiedEmail);
        return row.Item;
    }

    private static string? DisplayName(bool present, string? first, string? last, string? email) =>
        present ? UserDisplayNameRules.Resolve(first, last, email) : null;

    private async Task<List<PotentialStatusDto>> StatusesAsync(string potentialId)
    {
        var rows = await (
            from status in db.PotentialStatuses.AsNoTracking()
            where status.PotentialId == potentialId
            join stage in db.CrmStages.AsNoTracking() on status.CrmStageId equals stage.CrmStageId
            join user in db.Users.AsNoTracking() on status.CreatedByUserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            orderby status.StatusDate descending, status.CreatedAt descending
            select new StatusNameRow
            {
                Item = new PotentialStatusDto
                {
                    PotentialStatusId = status.PotentialStatusId,
                    PotentialId = status.PotentialId,
                    CrmStageId = status.CrmStageId,
                    StageName = stage.Name,
                    StatusDate = status.StatusDate,
                    Body = status.Body,
                    CreatedAt = status.CreatedAt
                },
                Present = user != null,
                First = user != null ? user.FirstName : null,
                Last = user != null ? user.LastName : null,
                Email = user != null ? user.Email : null
            }).ToListAsync();
        foreach (var row in rows)
            row.Item.CreatedByName = DisplayName(row.Present, row.First, row.Last, row.Email);
        return rows.Select(row => row.Item).ToList();
    }

    private async Task<List<CrmDiscussionDto>> DiscussionsAsync(string potentialId)
    {
        var rows = await (
            from note in db.CrmDiscussions.AsNoTracking()
            where note.PotentialId == potentialId
            join stage in db.CrmStages.AsNoTracking() on note.CrmStageId equals stage.CrmStageId
            join user in db.Users.AsNoTracking() on note.AuthorUserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            orderby note.CreatedAt descending
            select new DiscussionNameRow
            {
                Item = new CrmDiscussionDto
                {
                    CrmDiscussionId = note.CrmDiscussionId,
                    PotentialId = note.PotentialId,
                    CrmStageId = note.CrmStageId,
                    StageName = stage.Name,
                    Message = note.Message,
                    CreatedAt = note.CreatedAt
                },
                Present = user != null,
                First = user != null ? user.FirstName : null,
                Last = user != null ? user.LastName : null,
                Email = user != null ? user.Email : null
            }).ToListAsync();
        foreach (var row in rows)
            row.Item.AuthorName = DisplayName(row.Present, row.First, row.Last, row.Email);
        return rows.Select(row => row.Item).ToList();
    }

    private async Task<List<CrmAttachmentDto>> AttachmentsAsync(string potentialId)
    {
        var rows = await (
            from file in db.CrmAttachments.AsNoTracking()
            where file.PotentialId == potentialId
            join stage in db.CrmStages.AsNoTracking() on file.CrmStageId equals stage.CrmStageId
            join user in db.Users.AsNoTracking() on file.UploadedByUserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            orderby file.UploadedAt descending
            select new AttachmentNameRow
            {
                Item = new CrmAttachmentDto
                {
                    CrmAttachmentId = file.CrmAttachmentId,
                    PotentialId = file.PotentialId,
                    CrmStageId = file.CrmStageId,
                    StageName = stage.Name,
                    OriginalFileName = file.OriginalFileName,
                    MimeType = file.MimeType,
                    SizeBytes = file.SizeBytes,
                    UploadedAt = file.UploadedAt
                },
                Present = user != null,
                First = user != null ? user.FirstName : null,
                Last = user != null ? user.LastName : null,
                Email = user != null ? user.Email : null
            }).ToListAsync();
        foreach (var row in rows)
            row.Item.UploadedByName = DisplayName(row.Present, row.First, row.Last, row.Email);
        return rows.Select(row => row.Item).ToList();
    }

    private sealed class DetailNameRow
    {
        public PotentialDetailDto Item { get; set; } = new();
        public bool CreatedPresent { get; set; }
        public string? CreatedFirst { get; set; }
        public string? CreatedLast { get; set; }
        public string? CreatedEmail { get; set; }
        public bool ModifiedPresent { get; set; }
        public string? ModifiedFirst { get; set; }
        public string? ModifiedLast { get; set; }
        public string? ModifiedEmail { get; set; }
    }

    private sealed class StatusNameRow
    {
        public PotentialStatusDto Item { get; set; } = new();
        public bool Present { get; set; }
        public string? First { get; set; }
        public string? Last { get; set; }
        public string? Email { get; set; }
    }

    private sealed class DiscussionNameRow
    {
        public CrmDiscussionDto Item { get; set; } = new();
        public bool Present { get; set; }
        public string? First { get; set; }
        public string? Last { get; set; }
        public string? Email { get; set; }
    }

    private sealed class AttachmentNameRow
    {
        public CrmAttachmentDto Item { get; set; } = new();
        public bool Present { get; set; }
        public string? First { get; set; }
        public string? Last { get; set; }
        public string? Email { get; set; }
    }

    private async Task<IReadOnlyList<CrmRelationshipType>> RelationshipTypesAsync()
    {
        var json = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == Constants.SettingKeys.CrmRelationshipTypes)
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        return CrmRelationshipTypeRules.Parse(json);
    }

    private async Task<(string? Token, string? FolderId, IActionResult? Error)> TryGetCrmDriveAsync()
    {
        var cred = await db.AppDriveCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.AppDriveCredentialId == AppDriveLayoutRules.CredentialId);
        if (cred == null || string.IsNullOrEmpty(cred.EncryptedRefreshToken))
            return (null, null, new ObjectResult(AppDriveLayoutRules.NotConnectedMessage) { StatusCode = 409 });

        var folder = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == Constants.SettingKeys.CrmDriveParentFolderId)
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        var folderId = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim();
        if (string.IsNullOrEmpty(folderId))
            return (null, null, new ObjectResult("CRM folder is missing. Open App Settings and click Ensure folders.") { StatusCode = 409 });

        return (cred.EncryptedRefreshToken, folderId, null);
    }

    private async Task<IActionResult?> TryDeleteDriveFileAsync(string token, string fileId)
    {
        try
        {
            await drive.DeleteFileAsync(token, fileId);
            return null;
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception)
        {
            return new ObjectResult("A Drive file could not be removed.") { StatusCode = 409 };
        }
    }

    private async Task UpsertSettingAsync(string key, string value, string description)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row == null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value, Description = description });
            return;
        }

        row.Value = value;
    }

    private static OkObjectResult Page<T>(List<T> items, int total, int pageSize, int page)
    {
        var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return new OkObjectResult(new PagedResponse<T>
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

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? AsDate(DateTime? value) =>
        value.HasValue ? value.Value.Date : null;
}

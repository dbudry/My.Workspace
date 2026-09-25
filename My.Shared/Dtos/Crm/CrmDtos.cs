namespace My.Shared.Dtos.Crm;

public class OpportunityListItemDto
{
    public string OpportunityId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Stage { get; set; } = null!;
    public decimal? Amount { get; set; }
    public DateTime? ExpectedCloseDate { get; set; }
    public string? OrganizationId { get; set; }
    public string? OrganizationName { get; set; }
    public string? ContactId { get; set; }
    public string? ContactName { get; set; }
    public bool IsArchived { get; set; }
}

public class CrmActivityDto
{
    public string CrmActivityId { get; set; } = null!;
    public string OpportunityId { get; set; } = null!;
    public string ActivityType { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public string? Body { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? OwnerUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class OpportunityDetailDto : OpportunityListItemDto
{
    public string? Note { get; set; }
    public string? OwnerUserId { get; set; }
    public List<CrmActivityDto> Activities { get; set; } = new();
}

public class CreateOpportunityDto
{
    public string Name { get; set; } = null!;
    public string Stage { get; set; } = null!;
    public decimal? Amount { get; set; }
    public DateTime? ExpectedCloseDate { get; set; }
    public string? OrganizationId { get; set; }
    public string? ContactId { get; set; }
    public string? Note { get; set; }
}

public class UpdateOpportunityDto : CreateOpportunityDto
{
    public string OpportunityId { get; set; } = null!;
}

public class SetOpportunityArchivedDto
{
    public bool IsArchived { get; set; }
}

public class CreateCrmActivityDto
{
    public string OpportunityId { get; set; } = null!;
    public string ActivityType { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public string? Body { get; set; }
    public DateTimeOffset? DueAt { get; set; }
}

public class UpdateCrmActivityDto
{
    public string CrmActivityId { get; set; } = null!;
    public string ActivityType { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public string? Body { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

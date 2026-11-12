using System.Text.Json.Serialization;
using My.Shared.Serialization;

namespace My.Shared.Dtos.Crm;

public class CrmStageDto
{
    public string CrmStageId { get; set; } = "";
    public string Name { get; set; } = "";
    public int DefaultProbability { get; set; }
    public string Outcome { get; set; } = "";
    public int SortOrder { get; set; }
}

public class CreateCrmStageDto
{
    public string Name { get; set; } = "";
    public int DefaultProbability { get; set; }
    public string Outcome { get; set; } = "";
}

public class UpdateCrmStageDto : CreateCrmStageDto
{
    public string CrmStageId { get; set; } = "";
}

public class ReorderCrmStagesDto
{
    public List<string> StageIds { get; set; } = new();
}

public class CrmRelationshipTypeDto
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

public class SaveCrmRelationshipTypesDto
{
    public List<CrmRelationshipTypeDto> Types { get; set; } = new();
}

public class CrmNotifyUserDto
{
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public bool Selected { get; set; }
}

public class SaveCrmNotifyUsersDto
{
    public List<string> UserIds { get; set; } = new();
}

public class PotentialListItemDto
{
    public string PotentialId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? OrganizationId { get; set; }
    public string? OrganizationName { get; set; }
    public string? ContactId { get; set; }
    public string? ContactName { get; set; }
    public string CrmStageId { get; set; } = "";
    public string StageName { get; set; } = "";
    public string StageOutcome { get; set; } = "";
    public int Probability { get; set; }
    public decimal? Amount { get; set; }
    [JsonConverter(typeof(NullableCalendarDateJsonConverter))]
    public DateTime? ExpectedCloseDate { get; set; }
    public bool IsArchived { get; set; }
}

public class PotentialDetailDto : PotentialListItemDto
{
    public string? Description { get; set; }
    [JsonConverter(typeof(NullableCalendarDateJsonConverter))]
    public DateTime? ActualCloseDate { get; set; }
    public string? RelationshipType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CreatedByName { get; set; }
    public string? ModifiedByName { get; set; }
    public List<PotentialStatusDto> Statuses { get; set; } = new();
    public List<CrmDiscussionDto> Discussions { get; set; } = new();
    public List<CrmAttachmentDto> Attachments { get; set; } = new();
}

public class CreatePotentialDto
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string CrmStageId { get; set; } = "";
    public int Probability { get; set; }
    public decimal? Amount { get; set; }
    [JsonConverter(typeof(NullableCalendarDateJsonConverter))]
    public DateTime? ExpectedCloseDate { get; set; }
    [JsonConverter(typeof(NullableCalendarDateJsonConverter))]
    public DateTime? ActualCloseDate { get; set; }
    public string? OrganizationId { get; set; }
    public string? ContactId { get; set; }
    /// <summary>Null leaves the contact's type unchanged. Blank clears it.</summary>
    public string? RelationshipType { get; set; }
}

public class UpdatePotentialDto : CreatePotentialDto
{
    public string PotentialId { get; set; } = "";
}

public class SetPotentialArchivedDto
{
    public bool IsArchived { get; set; }
}

public class PotentialStatusDto
{
    public string PotentialStatusId { get; set; } = "";
    public string PotentialId { get; set; } = "";
    public string CrmStageId { get; set; } = "";
    public string StageName { get; set; } = "";
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime StatusDate { get; set; }
    public string Body { get; set; } = "";
    public string? CreatedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CreatePotentialStatusDto
{
    public string CrmStageId { get; set; } = "";
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime StatusDate { get; set; }
    public string Body { get; set; } = "";
}

public class CrmDiscussionDto
{
    public string CrmDiscussionId { get; set; } = "";
    public string PotentialId { get; set; } = "";
    public string CrmStageId { get; set; } = "";
    public string StageName { get; set; } = "";
    public string? AuthorName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Message { get; set; } = "";
    /// <summary>Set on the create response when the comment was saved and the email was not sent.</summary>
    public string? EmailWarning { get; set; }
}

public class CreateCrmDiscussionDto
{
    public string Message { get; set; } = "";
}

public class CrmAttachmentDto
{
    public string CrmAttachmentId { get; set; } = "";
    public string PotentialId { get; set; } = "";
    public string CrmStageId { get; set; } = "";
    public string StageName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public int SizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public string? UploadedByName { get; set; }
}

public class UploadCrmAttachmentDto
{
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string ContentBase64 { get; set; } = "";
    public string? CrmStageId { get; set; }
}

public class MoveCrmAttachmentStageDto
{
    public string CrmStageId { get; set; } = "";
}

public class CrmAttachmentContentDto
{
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string ContentBase64 { get; set; } = "";
}

public class CrmReportRowDto
{
    public string PotentialId { get; set; } = "";
    public string? AccountName { get; set; }
    public string PotentialName { get; set; } = "";
    public string? Description { get; set; }
    public string StageName { get; set; } = "";
    public int Probability { get; set; }
    public decimal? Amount { get; set; }
    [JsonConverter(typeof(NullableCalendarDateJsonConverter))]
    public DateTime? ClosingDate { get; set; }
    public string? Status { get; set; }
}

public class CrmDashboardStageDto
{
    public string CrmStageId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Outcome { get; set; } = "";
    public int SortOrder { get; set; }
    public int Count { get; set; }
    public decimal ForecastRevenue { get; set; }
    public decimal ExpectedRevenue { get; set; }
}

public class CrmDashboardDto
{
    public List<CrmDashboardStageDto> Stages { get; set; } = new();
    public decimal OpenExpectedRevenue { get; set; }
}

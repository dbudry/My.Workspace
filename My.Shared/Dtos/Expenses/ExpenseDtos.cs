using System.Text.Json.Serialization;
using My.Shared.Serialization;

namespace My.Shared.Dtos.Expenses;

public class ExpenseReportListDto
{
    public string ExpenseReportId { get; set; } = null!;
    public string UserId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime CoverStart { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime CoverEnd { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime ReportDate { get; set; }
    public string Status { get; set; } = null!;
    public int LineCount { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReimbursedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ExpenseLineDto
{
    public string? ExpenseLineId { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal? Miles { get; set; }
    public string? TransportationCode { get; set; }
    public string? MiscellaneousCode { get; set; }
    public bool MealBreakfast { get; set; }
    public bool MealLunch { get; set; }
    public bool MealDinner { get; set; }
    public int SortOrder { get; set; }
    public List<ExpenseReceiptDto> Receipts { get; set; } = [];
}

public class ExpenseReceiptDto
{
    public string ExpenseReceiptId { get; set; } = null!;
    public string OriginalFileName { get; set; } = null!;
    public string MimeType { get; set; } = null!;
    public int SizeBytes { get; set; }
}

public class UploadExpenseReceiptDto
{
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string ContentBase64 { get; set; } = "";
}

public class ExpenseReportDto
{
    public string ExpenseReportId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime CoverStart { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime CoverEnd { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime ReportDate { get; set; }
    public string Status { get; set; } = null!;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReimbursedAt { get; set; }
    public string? Purpose { get; set; }
    public string? PlantOrLocation { get; set; }
    public string? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string? ChargeToNote { get; set; }
    public string? EmployeeNameSnapshot { get; set; }
    public string? AddressSnapshot { get; set; }
    public decimal MileageRateSnapshot { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ExpenseLineDto> Lines { get; set; } = [];
    public decimal TotalAmount { get; set; }
}

public class CreateExpenseReportDto
{
    public int Year { get; set; }
    public int Month { get; set; }
}

public class UpdateExpenseReportDto
{
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime CoverStart { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime CoverEnd { get; set; }
    public string? Purpose { get; set; }
    public List<ExpenseLineDto> Lines { get; set; } = [];
}

public class ExpenseContextDto
{
    public string? HomeOrganizationId { get; set; }
    public string? HomeOrganizationName { get; set; }
    public decimal MileageRatePerMile { get; set; }
    public List<ExpenseChoiceDto> Categories { get; set; } = [];
    public List<ExpenseChoiceDto> TransportationCodes { get; set; } = [];
    public List<ExpenseChoiceDto> MiscellaneousCodes { get; set; } = [];
}

public class ExpenseChoiceDto
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}

public class ExpenseSettingsDto
{
    public decimal MileageRatePerMile { get; set; }
    public bool HasApprovalSignature { get; set; }
}

public class UploadExpenseSignatureDto
{
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string ContentBase64 { get; set; } = "";
}

public class UpdateExpenseSettingsDto
{
    public decimal MileageRatePerMile { get; set; }
}

public class ExpenseDataExportDto
{
    public List<ExpenseReportExportRow> Reports { get; set; } = [];
    public List<ExpenseLineExportRow> Lines { get; set; } = [];
    public List<ExpenseReceiptExportRow> Receipts { get; set; } = [];
}

public class ExpenseReportExportRow
{
    public string ExpenseReportId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime CoverStart { get; set; }
    public DateTime CoverEnd { get; set; }
    public string Status { get; set; } = "";
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReimbursedAt { get; set; }
    public int LineCount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal MileageRateSnapshot { get; set; }
}

public class ExpenseLineExportRow
{
    public string ExpenseLineId { get; set; } = "";
    public string ExpenseReportId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public string Status { get; set; } = "";
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal? Miles { get; set; }
    public string? TransportationCode { get; set; }
    public string? MiscellaneousCode { get; set; }
    public bool MealBreakfast { get; set; }
    public bool MealLunch { get; set; }
    public bool MealDinner { get; set; }
    public int ReceiptCount { get; set; }
}

public class ExpenseReceiptExportRow
{
    public string ExpenseReceiptId { get; set; } = "";
    public string ExpenseLineId { get; set; } = "";
    public string ExpenseReportId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public int SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
}

public class AppDriveStatusDto
{
    public bool Connected { get; set; }
    public string? Email { get; set; }
    public string? SharedDriveId { get; set; }
    public string? SharedDriveName { get; set; }
    public string? IntranetFolderId { get; set; }
    public string? ExpensesFolderId { get; set; }
    public bool? DomainUsersOnly { get; set; }
    public bool? DriveMembersOnly { get; set; }
    public bool? SharingFoldersRequiresOrganizerPermission { get; set; }
    public bool? CopyRequiresWriterPermission { get; set; }
    public bool? RestrictedForWriters { get; set; }
}

using System.Text.Json.Serialization;
using My.Shared.Rules;
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
    public decimal GrossAmount { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReimbursedAt { get; set; }
    public bool HasPaymentDate { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime PaymentDate { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Purpose { get; set; }
    public string? PlantOrLocation { get; set; }
    public string? ChargeToNote { get; set; }
    public string? ChargeToOrganizationId { get; set; }
    public string? ChargeToName { get; set; }
    public List<ExpenseLineSearchDto> Lines { get; set; } = [];
}

public class ExpenseLineSearchDto
{
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal? Miles { get; set; }
    public string? TransportationCode { get; set; }
    public string? MiscellaneousCode { get; set; }
    public string? MealType { get; set; }
    public int? HeadCount { get; set; }
    public List<string> ReceiptFileNames { get; set; } = [];
}

public class ExpenseLineDto
{
    public string? ExpenseLineId { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal SpentAmount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public decimal ExchangeRateToUsd { get; set; } = 1m;
    public bool IsReturned { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime ReturnedOn { get; set; }
    public decimal? CreditAmount { get; set; }
    public string? CreditCurrencyCode { get; set; }
    public string? CreditDescription { get; set; }
    public decimal? Miles { get; set; }
    public string? TransportationCode { get; set; }
    public string? MiscellaneousCode { get; set; }
    public string? MealType { get; set; }
    public int? HeadCount { get; set; }
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
    public string? ChargeToOrganizationId { get; set; }
    public string? ChargeToName { get; set; }
    public string? EmployeeNameSnapshot { get; set; }
    public string? AddressSnapshot { get; set; }
    public decimal MileageRateSnapshot { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ExpenseLineDto> Lines { get; set; } = [];
    public List<ExpenseFormCreditDto> FormCredits { get; set; } = [];
    public decimal TotalAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public bool CreditBucketEnabled { get; set; }
    public string CreditEntryMode { get; set; } = ExpenseCreditEntryRules.FromLine;
    public bool UserEntersRates { get; set; }
    public string CompanyCurrencyCode { get; set; } = "USD";
    public bool HasPaymentDate { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime PaymentDate { get; set; }
    public List<ExpenseCreditSourceDto> CreditSources { get; set; } = [];
    public List<ExpenseCreditLineDto> OtherCreditLines { get; set; } = [];
}

public class ExpenseCreditSourceDto
{
    public string LineId { get; set; } = "";
    public string ReportId { get; set; } = "";
    public string? ChargeToOrganizationId { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime ReturnDate { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public decimal SpentAmount { get; set; }
    public decimal ExchangeRateToUsd { get; set; } = 1m;
    public decimal AmountUsd { get; set; }
}

public class ExpenseCreditLineDto
{
    public string LineId { get; set; } = "";
    public string ReportId { get; set; } = "";
    public string? ChargeToOrganizationId { get; set; }
    public string? ReportStatus { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime Date { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public decimal SpentAmount { get; set; }
    public decimal ExchangeRateToUsd { get; set; } = 1m;
    public decimal AmountUsd { get; set; }
    public int SortOrder { get; set; }
    public bool IsReturned { get; set; }
}

public class ReimburseExpenseReportDto
{
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime PaymentDate { get; set; }
}

public class ReturnExpenseLineDto
{
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime ReturnDate { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "";
}

public class ExpenseFormCreditDto
{
    public string? ExpenseCreditId { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public decimal ExchangeRateToUsd { get; set; } = 1m;
}

public class CreateExpenseReportDto
{
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime CoverStart { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime CoverEnd { get; set; }
    public string? ChargeToOrganizationId { get; set; }
}

public class UpdateExpenseReportDto
{
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime CoverStart { get; set; }
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime CoverEnd { get; set; }
    public string? Purpose { get; set; }
    public string? ChargeToOrganizationId { get; set; }
    public List<ExpenseLineDto> Lines { get; set; } = [];
    public List<ExpenseFormCreditDto> FormCredits { get; set; } = [];
}

public class ExpenseContextDto
{
    public string? HomeOrganizationId { get; set; }
    public string? HomeOrganizationName { get; set; }
    public decimal MileageRatePerMile { get; set; }
    public List<ExpenseChoiceDto> Categories { get; set; } = [];
    public List<ExpenseChoiceDto> TransportationCodes { get; set; } = [];
    public List<ExpenseChoiceDto> MiscellaneousCodes { get; set; } = [];
    public List<ExpenseCurrencyDto> Currencies { get; set; } = [];
    public bool CreditBucketEnabled { get; set; }
    public string CreditEntryMode { get; set; } = ExpenseCreditEntryRules.FromLine;
    public bool UserEntersRates { get; set; }
    public string CompanyCurrencyCode { get; set; } = "USD";
    public string DefaultCurrencyCode { get; set; } = "USD";
    public string EditorView { get; set; } = "Detail";
    public string CurrencyLayout { get; set; } = "Mixed";
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
    public List<ExpenseCodeDto> MiscellaneousCodes { get; set; } = [];
    public List<ExpenseCodeDto> TransportationCodes { get; set; } = [];
    public List<ExpenseNotifyUserDto> NotifyUsers { get; set; } = [];
    public List<ExpenseCurrencyDto> Currencies { get; set; } = [];
    public bool CreditBucketEnabled { get; set; }
    public string CreditEntryMode { get; set; } = ExpenseCreditEntryRules.FromLine;
    public bool UserEntersRates { get; set; }
    public string CompanyCurrencyCode { get; set; } = "USD";
}

public class ExpenseCurrencyDto
{
    public string ExpenseCurrencyId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#1565C0";
    public decimal RateToUsd { get; set; } = 1m;
    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; }
    public int SortOrder { get; set; }
}

public class UpsertExpenseCurrencyDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public decimal RateToUsd { get; set; } = 1m;
    public bool IsActive { get; set; } = true;
}

public class ExpenseCodeDto
{
    public string ExpenseCodeId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsLocked { get; set; }
    public int SortOrder { get; set; }
}

public class UpsertExpenseCodeDto
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class ExpenseNotifyUserDto
{
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public class UpdateExpenseNotifyUsersDto
{
    public List<string> UserIds { get; set; } = [];
}

public class SubmitExpenseReportResultDto
{
    public ExpenseReportDto Report { get; set; } = null!;
    public string? EmailWarning { get; set; }
}

public class ExpensePdfTarget
{
    public string? ChargeToOrganizationId { get; set; }
    public string Name { get; set; } = "";
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
    public bool CreditBucketEnabled { get; set; }
    public bool UserEntersRates { get; set; }
    public string CompanyCurrencyCode { get; set; } = "USD";
    public string CreditEntryMode { get; set; } = ExpenseCreditEntryRules.FromLine;
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
    public string? ChargeToOrganizationId { get; set; }
    public string? ChargeToName { get; set; }
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
    public decimal SpentAmount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public decimal ExchangeRateToUsd { get; set; } = 1m;
    [JsonConverter(typeof(CalendarDateJsonConverter))]
    public DateTime ReturnedOn { get; set; }
    public bool IsReturned { get; set; }
    public decimal? Miles { get; set; }
    public string? TransportationCode { get; set; }
    public string? MiscellaneousCode { get; set; }
    public string? MealType { get; set; }
    public int? HeadCount { get; set; }
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

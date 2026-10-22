using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.DAL.Data;
using My.DAL.Models;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Functions.Services;
using My.Shared.Constants;
using My.Shared.Dtos.Expenses;
using My.Shared.Rules;
using My.Shared.Validation;

namespace My.Functions
{
    public partial class ExpenseFunction
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IValidator<CreateExpenseReportDto> _createValidator;
        private readonly IValidator<UpdateExpenseReportDto> _updateValidator;
        private readonly IValidator<UpdateExpenseSettingsDto> _settingsValidator;
        private readonly GoogleDriveService _drive;
        private readonly ExpenseStatementPdfService _pdf;
        private readonly ExpenseSubmitMailer _mailer;
        private readonly ILogger<ExpenseFunction> _logger;

        public ExpenseFunction(
            ApplicationDbContext dbContext,
            IValidator<CreateExpenseReportDto> createValidator,
            IValidator<UpdateExpenseReportDto> updateValidator,
            IValidator<UpdateExpenseSettingsDto> settingsValidator,
            GoogleDriveService drive,
            ExpenseStatementPdfService pdf,
            ExpenseSubmitMailer mailer,
            ILogger<ExpenseFunction> logger)
        {
            _dbContext = dbContext;
            _createValidator = createValidator;
            _updateValidator = updateValidator;
            _settingsValidator = settingsValidator;
            _drive = drive;
            _pdf = pdf;
            _mailer = mailer;
            _logger = logger;
        }

        [Function("GetExpenseContext")]
        public async Task<IActionResult> GetContextAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/context")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth) return unauth;

            var settings = await LoadExpenseSettingsAsync();
            string? homeName = null;
            if (!string.IsNullOrEmpty(settings.HomeOrganizationId))
            {
                var org = await _dbContext.Organizations.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.OrganizationId == settings.HomeOrganizationId);
                homeName = org?.Name;
            }

            var codes = await LoadCodesAsync();
            var transportRows = codes.Where(row => row.Kind == ExpenseCodeKindRules.Transportation).ToList();
            var miscRows = codes.Where(row => row.Kind == ExpenseCodeKindRules.Miscellaneous).ToList();
            var currencies = await _dbContext.ExpenseCurrencies.AsNoTracking()
                .Where(row => row.IsActive && !row.IsRemoved)
                .OrderBy(row => row.SortOrder)
                .ThenBy(row => row.Code)
                .ToListAsync();
            var prefs = await _dbContext.UserSettings.AsNoTracking()
                .FirstOrDefaultAsync(row => row.UserId == userId);
            var policy = await LoadExpensePolicyAsync();
            var defaultCurrency = ExpenseCurrencyRules.OptionalCode(prefs?.ExpenseDefaultCurrencyCode) ?? "";
            if (currencies.All(row => !string.Equals(row.Code, defaultCurrency, StringComparison.Ordinal)))
            {
                defaultCurrency = currencies.Any(row =>
                    string.Equals(row.Code, policy.CompanyCurrencyCode, StringComparison.Ordinal))
                    ? policy.CompanyCurrencyCode
                    : ExpenseCurrencyRules.UsdCode;
            }

            return new OkObjectResult(new ExpenseContextDto
            {
                HomeOrganizationId = settings.HomeOrganizationId,
                HomeOrganizationName = homeName,
                MileageRatePerMile = settings.MileageRate,
                Categories = ExpenseCategoryRules.CategoryChoices
                    .Select(c => new ExpenseChoiceDto { Value = c.Value, Label = c.Label })
                    .ToList(),
                TransportationCodes = transportRows.Count == 0
                    ? FallbackChoices(ExpenseCategoryRules.TransportationCodes)
                    : ToChoices(transportRows),
                MiscellaneousCodes = miscRows.Count == 0
                    ? FallbackChoices(ExpenseCategoryRules.MiscellaneousCodes)
                    : ToChoices(miscRows),
                Currencies = currencies.Select(ToCurrencyDto).ToList(),
                CreditBucketEnabled = policy.CreditBucketEnabled,
                CreditEntryMode = policy.CreditEntryMode,
                UserEntersRates = policy.UserEntersRates,
                CompanyCurrencyCode = policy.CompanyCurrencyCode,
                DefaultCurrencyCode = defaultCurrency,
                EditorView = ExpenseCurrencyRules.NormalizeView(prefs?.ExpenseEditorView),
                CurrencyLayout = ExpenseCurrencyRules.NormalizeLayout(prefs?.ExpenseCurrencyLayout)
            });
        }

        [Function("GetExpenseSettings")]
        public async Task<IActionResult> GetSettingsAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/settings")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            return new OkObjectResult(await LoadExpenseSettingsDtoAsync());
        }

        [Function("UpdateExpenseSettings")]
        public async Task<IActionResult> UpdateSettingsAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "expenses/settings")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, _settingsValidator);
            if (validationError != null)
                return validationError;

            await UpsertSettingAsync(
                Constants.SettingKeys.ExpensesMileageRatePerMile,
                ExpenseMileageRateRules.ToStorage(dto!.MileageRatePerMile),
                "Personal-car mileage reimbursement rate in USD per mile.");
            await _dbContext.SaveChangesAsync();

            return new OkObjectResult(await LoadExpenseSettingsDtoAsync());
        }

        [Function("GetExpenseApprovalSignatureMedia")]
        public async Task<IActionResult> GetApprovalSignatureMediaAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/settings/signature/media")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var (bytes, mime) = await LoadApprovalSignatureAsync();
            if (bytes == null)
                return new NotFoundObjectResult("No approval signature.");
            return new FileContentResult(bytes, mime ?? "image/png");
        }

        [Function("PutExpenseApprovalSignature")]
        public async Task<IActionResult> PutApprovalSignatureAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "expenses/settings/signature")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var parsed = await ReadSignaturePayloadAsync(req);
            if (parsed.Error != null)
                return parsed.Error;

            await UpsertSettingAsync(
                Constants.SettingKeys.ExpensesApprovalSignature,
                Convert.ToBase64String(parsed.Bytes!),
                "Manager approval signature image for the expense form.");
            await UpsertSettingAsync(
                Constants.SettingKeys.ExpensesApprovalSignatureMime,
                parsed.Mime!,
                "MIME type of the manager approval signature.");
            await _dbContext.SaveChangesAsync();
            return new OkObjectResult(await LoadExpenseSettingsDtoAsync());
        }

        [Function("DeleteExpenseApprovalSignature")]
        public async Task<IActionResult> DeleteApprovalSignatureAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "expenses/settings/signature")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var row = await _dbContext.AppSettings.FirstOrDefaultAsync(s =>
                s.Key == Constants.SettingKeys.ExpensesApprovalSignature);
            if (row != null)
                row.Value = "";
            var mime = await _dbContext.AppSettings.FirstOrDefaultAsync(s =>
                s.Key == Constants.SettingKeys.ExpensesApprovalSignatureMime);
            if (mime != null)
                mime.Value = "";
            await _dbContext.SaveChangesAsync();
            return new OkObjectResult(await LoadExpenseSettingsDtoAsync());
        }

        [Function("GetExpenseReportPdf")]
        public async Task<IActionResult> GetPdfAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/reports/{id}/pdf")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth)
                return unauth;

            var report = await _dbContext.ExpenseReports
                .Include(r => r.Lines).ThenInclude(l => l.Receipts)
                .Include(r => r.Department)
                .Include(r => r.FiledPdfs)
                .FirstOrDefaultAsync(r => r.ExpenseReportId == id);
            if (report == null || !ExpenseReportRules.CanView(report.UserId, userId, IsExpensesManager(principal)))
                return new NotFoundObjectResult("Expense report not found.");

            var (chargeSpecified, chargeKey) = ExpenseChargeToRules.ReadQuery(req.Query["chargeTo"]);
            try
            {
                if (report.FiledPdfs.Count > 1 && !chargeSpecified)
                    return new BadRequestObjectResult(
                        "This report has more than one Charge to. Open each form from the PDF menu.");

                if (report.FiledPdfs.Count > 0)
                {
                    var filed = await TryDownloadFiledPacketAsync(report, chargeSpecified, chargeKey);
                    if (filed == null)
                        return new ObjectResult("Couldn't load the filed expense PDF.") { StatusCode = 502 };
                    return new FileContentResult(filed.Value.Bytes, filed.Value.ContentType)
                    {
                        FileDownloadName = filed.Value.FileName
                    };
                }

                var pdf = await BuildReportPdfAsync(report, chargeSpecified, chargeKey);
                return new FileContentResult(pdf.Bytes, pdf.ContentType)
                {
                    FileDownloadName = pdf.FileName
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expense PDF failed for {ReportId}.", id);
                var message = "Couldn't build the expense PDF.";
                return new ObjectResult(message) { StatusCode = 502 };
            }
        }

        [Function("GetExpenseReports")]
        public async Task<IActionResult> GetMineAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/reports")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth) return unauth;

            var rows = await LoadListAsync(
                _dbContext.ExpenseReports.AsNoTracking()
                    .Where(r => r.UserId == userId)
                    .OrderByDescending(r => r.CoverStart).ThenByDescending(r => r.CreatedAt));

            return new OkObjectResult(rows);
        }

        [Function("GetTeamExpenseReports")]
        public async Task<IActionResult> GetTeamAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/team")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            if (!ExpenseDataExtractionRules.TryParseStatus(
                    req.Query["status"],
                    out var status,
                    out var statusError,
                    ExpenseDataExtractionRules.StatusAll))
                return new BadRequestObjectResult(statusError);

            var userIdFilter = req.Query["userId"];
            var years = ExpenseListFilterRules.ParseInts(req.Query["years"]);
            if (years.Count == 0 && int.TryParse(req.Query["year"], out var y))
                years.Add(y);
            var months = ExpenseListFilterRules.ParseInts(req.Query["months"]);
            if (months.Count == 0 && int.TryParse(req.Query["month"], out var mo))
                months.Add(mo);

            var query = _dbContext.ExpenseReports.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(userIdFilter))
                query = query.Where(r => r.UserId == userIdFilter);
            query = FilterByFilingMonth(query, years, months);
            query = FilterByStatus(query, status);

            var rows = await LoadListAsync(
                query.OrderByDescending(r => r.CoverStart)
                    .ThenBy(r => r.EmployeeNameSnapshot));

            return new OkObjectResult(rows);
        }

        [Function("GetExpenseData")]
        public async Task<IActionResult> GetDataAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/data")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            if (!ExpenseDataExtractionRules.TryParseEntities(req.Query["Entities"], out var entities, out var parseError))
                return new BadRequestObjectResult(parseError);
            if (!ExpenseDataExtractionRules.TryParseStatus(req.Query["Status"], out var status, out var statusError))
                return new BadRequestObjectResult(statusError);

            int? year = int.TryParse(req.Query["Year"], out var y) ? y : null;
            int? month = int.TryParse(req.Query["Month"], out var m) ? m : null;
            if (ExpenseDataExtractionRules.ValidateRequest(entities, year, month) is { } validationError)
                return new BadRequestObjectResult(validationError);

            var userIds = new HashSet<string>(StringComparer.Ordinal);
            var rawUserIds = req.Query["UserIds"];
            if (!string.IsNullOrWhiteSpace(rawUserIds))
            {
                foreach (var id in rawUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    userIds.Add(id);
            }

            var query = _dbContext.ExpenseReports.AsNoTracking()
                .Include(r => r.Lines).ThenInclude(l => l.Receipts)
                .AsQueryable();
            if (year.HasValue || month.HasValue)
            {
                query = FilterByFilingMonth(
                    query,
                    year.HasValue ? [year.Value] : [],
                    month.HasValue ? [month.Value] : []);
            }
            if (userIds.Count > 0)
                query = query.Where(r => userIds.Contains(r.UserId));
            query = FilterByStatus(query, status);

            var reports = await query
                .OrderBy(r => r.Year).ThenBy(r => r.Month).ThenBy(r => r.EmployeeNameSnapshot)
                .ToListAsync();

            return new OkObjectResult(BuildExport(reports, entities));
        }

        [Function("GetExpenseReport")]
        public async Task<IActionResult> GetByIdAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/reports/{id}")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth) return unauth;

            var report = await LoadReportAsync(id, userId, IsExpensesManager(principal), tracking: false);
            if (report == null)
                return new NotFoundObjectResult("Expense report not found.");

            return new OkObjectResult(await ToDtoAsync(report));
        }

        [Function("SubmitExpenseReport")]
        public async Task<IActionResult> SubmitAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses/reports/{id}/submit")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth) return unauth;

            var report = await LoadOwnedReportAsync(id, userId, tracking: true);
            if (report == null)
                return new NotFoundObjectResult("Expense report not found.");

            var blocked = ExpenseReportRules.SubmitBlockedReason(
                report.Status,
                report.Lines.Count,
                report.ChargeToOrganizationId);
            if (blocked != null)
                return new ConflictObjectResult(blocked);

            var submittedAt = DateTime.UtcNow;
            var submitterPrefs = await _dbContext.UserSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId);
            var (submitYear, submitMonth) = ExpenseReportRules.SubmitMonth(submittedAt, submitterPrefs?.TimeZone);
            var previousYear = report.Year;
            var previousMonth = report.Month;
            report.Year = submitYear;
            report.Month = submitMonth;

            List<ExpensePacket> packets;
            var uploadedThisAttempt = new List<string>();
            try
            {
                packets = await BuildPacketsAsync(report);
                var previousByKey = report.FiledPdfs.ToDictionary(
                    file => ExpenseChargeToRules.Key(file.ChargeToOrganizationId),
                    file => file.DriveFileId,
                    StringComparer.Ordinal);
                foreach (var packet in packets)
                {
                    previousByKey.TryGetValue(packet.ChargeKey, out var previousId);
                    var driveName = ExpenseDriveNamingRules.FiledPacketFileName(
                        report.Year, report.Month, report.EmployeeNameSnapshot, report.ExpenseReportId,
                        packets.Count > 1 ? packet.ChargeName : null);
                    packet.DriveFileId = await UploadFiledPacketAsync(
                        report, packet.Bytes, driveName, previousId);
                    if (!string.IsNullOrEmpty(packet.DriveFileId)
                        && !string.Equals(packet.DriveFileId, previousId, StringComparison.Ordinal))
                        uploadedThisAttempt.Add(packet.DriveFileId);
                }
            }
            catch (Exception ex)
            {
                report.Year = previousYear;
                report.Month = previousMonth;
                foreach (var uploadedId in uploadedThisAttempt)
                    await TryDeleteFiledPacketAsync(report, uploadedId);
                _logger.LogError(ex, "Could not file expense PDF for {ReportId}.", id);
                var message = ex is InvalidOperationException
                    ? ex.Message
                    : "Couldn't file the expense PDF. The report was not submitted.";
                return new ObjectResult(message) { StatusCode = 502 };
            }

            var replacedIds = packets.Select(packet => packet.DriveFileId).ToHashSet(StringComparer.Ordinal);
            var staleIds = report.FiledPdfs
                .Select(file => file.DriveFileId)
                .Where(id => !string.IsNullOrEmpty(id) && !replacedIds.Contains(id))
                .ToList();
            foreach (var existing in report.FiledPdfs.ToList())
                report.FiledPdfs.Remove(existing);
            var sort = 0;
            foreach (var packet in packets)
            {
                report.FiledPdfs.Add(new ExpenseFiledPdf
                {
                    ExpenseFiledPdfId = Guid.NewGuid().ToString("N"),
                    ExpenseReportId = report.ExpenseReportId,
                    ChargeToOrganizationId = string.IsNullOrEmpty(packet.ChargeKey) ? null : packet.ChargeKey,
                    ChargeToName = packet.ChargeName,
                    DriveFileId = packet.DriveFileId!,
                    FormVersion = ExpenseFormVersionRules.Current,
                    SortOrder = sort++
                });
            }

            report.Status = ExpenseStatusRules.Submitted;
            report.SubmittedAt = submittedAt;
            report.SubmittedByUserId = userId;
            report.UpdatedAt = submittedAt;
            try
            {
                await RelocateExpenseReceiptsAsync(report);
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                foreach (var uploadedId in uploadedThisAttempt)
                    await TryDeleteFiledPacketAsync(report, uploadedId);
                _logger.LogError(ex, "Could not save submitted expense {ReportId}. The new PDF was removed.", id);
                return new ObjectResult("Couldn't file the expense PDF. The report was not submitted.")
                {
                    StatusCode = 502
                };
            }

            foreach (var staleId in staleIds)
                await TryDeleteFiledPacketAsync(report, staleId);

            var emailWarning = await TrySendSubmitEmailAsync(report, packets);
            var reloaded = await LoadOwnedReportAsync(report.ExpenseReportId, userId, tracking: false);
            return new OkObjectResult(new SubmitExpenseReportResultDto
            {
                Report = await ToDtoAsync(reloaded!),
                EmailWarning = emailWarning
            });
        }

        [Function("UnsubmitExpenseReport")]
        public async Task<IActionResult> UnsubmitAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses/reports/{id}/unsubmit")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var report = await _dbContext.ExpenseReports
                .Include(r => r.Lines)
                .Include(r => r.Department)
                .Include(r => r.FiledPdfs)
                .FirstOrDefaultAsync(r => r.ExpenseReportId == id);
            if (report == null)
                return new NotFoundObjectResult("Expense report not found.");

            var blocked = ExpenseReportRules.UnsubmitBlockedReason(report.Status);
            if (blocked != null)
                return new ConflictObjectResult(blocked);

            var filedIds = report.FiledPdfs.Select(file => file.DriveFileId).ToList();
            report.FiledPdfs.Clear();
            report.Status = ExpenseStatusRules.Draft;
            report.SubmittedAt = null;
            report.SubmittedByUserId = null;
            report.ReimbursedAt = null;
            report.ReimbursedByUserId = null;
            foreach (var line in report.Lines)
                ClearLineCredit(line);
            report.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            foreach (var filedId in filedIds)
                await TryDeleteFiledPacketAsync(report, filedId);

            var reloaded = await LoadReportAsync(report.ExpenseReportId, userId, isManager: true, tracking: false);
            return new OkObjectResult(await ToDtoAsync(reloaded!));
        }

        [Function("ReimburseExpenseReport")]
        public async Task<IActionResult> ReimburseAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses/reports/{id}/reimburse")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var report = await _dbContext.ExpenseReports
                .Include(r => r.Lines)
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.ExpenseReportId == id);
            if (report == null)
                return new NotFoundObjectResult("Expense report not found.");

            var blocked = ExpenseReportRules.ReimburseBlockedReason(report.Status);
            if (blocked != null)
                return new ConflictObjectResult(blocked);

            var (pay, payError) = await RequestValidator.ReadJsonAndValidateAsync(
                req, new ReimburseExpenseReportDtoValidator());
            if (payError != null)
                return payError;

            var payZone = await OwnerTimeZoneAsync(report.UserId);
            report.Status = ExpenseStatusRules.Reimbursed;
            report.ReimbursedAt = ExpensePaymentDateRules.ToStoredUtc(pay!.PaymentDate, payZone);
            report.ReimbursedByUserId = userId;
            report.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var reloaded = await LoadReportAsync(report.ExpenseReportId, userId, isManager: true, tracking: false);
            return new OkObjectResult(await ToDtoAsync(reloaded!));
        }

        [Function("UndoReimburseExpenseReport")]
        public async Task<IActionResult> UndoReimburseAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses/reports/{id}/undo-reimburse")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var report = await _dbContext.ExpenseReports
                .Include(r => r.Lines)
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.ExpenseReportId == id);
            if (report == null)
                return new NotFoundObjectResult("Expense report not found.");

            var blocked = ExpenseReportRules.UndoReimburseBlockedReason(report.Status);
            if (blocked != null)
                return new ConflictObjectResult(blocked);

            report.Status = ExpenseStatusRules.Submitted;
            report.ReimbursedAt = null;
            report.ReimbursedByUserId = null;
            foreach (var line in report.Lines)
                ClearLineCredit(line);
            report.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var reloaded = await LoadReportAsync(report.ExpenseReportId, userId, isManager: true, tracking: false);
            return new OkObjectResult(await ToDtoAsync(reloaded!));
        }

        [Function("CreateExpenseReport")]
        public async Task<IActionResult> CreateAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses/reports")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth) return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, _createValidator);
            if (validationError != null)
                return validationError;

            var user = await _dbContext.ApplicationUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return new NotFoundObjectResult("User not found.");

            var settings = await LoadExpenseSettingsAsync();
            var userPrefs = await _dbContext.UserSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId);
            var coverStart = ExpenseLineRules.CalendarDate(dto!.CoverStart);
            var coverEnd = ExpenseLineRules.CalendarDate(dto.CoverEnd);
            if (!ExpenseReportRules.IsValidCoverPeriod(coverStart, coverEnd))
                return new BadRequestObjectResult("Cover end date cannot be before cover start date.");
            var now = DateTime.UtcNow;

            var report = new ExpenseReport
            {
                ExpenseReportId = Guid.NewGuid().ToString("N"),
                UserId = userId,
                Year = 0,
                Month = 0,
                CoverStart = coverStart,
                CoverEnd = coverEnd,
                ReportDate = coverEnd,
                Status = ExpenseStatusRules.Draft,
                PlantOrLocation = ExpenseReportRules.DefaultPlantOrLocation,
                EmployeeNameSnapshot = ExpenseReportRules.EmployeeDisplayName(user.FirstName, user.LastName),
                AddressSnapshot = userPrefs?.ExpenseHomeAddress,
                MileageRateSnapshot = settings.MileageRate,
                CreatedAt = now,
                UpdatedAt = now
            };

            var chargeError = await ApplyChargeToAsync(report, dto.ChargeToOrganizationId, required: true);
            if (chargeError != null)
                return chargeError;

            _dbContext.ExpenseReports.Add(report);
            await _dbContext.SaveChangesAsync();

            return new OkObjectResult(await ToDtoAsync(report));
        }

        [Function("UpdateExpenseReport")]
        public async Task<IActionResult> UpdateAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "expenses/reports/{id}")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth) return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, _updateValidator);
            if (validationError != null)
                return validationError;

            var report = await LoadOwnedReportAsync(id, userId, tracking: true);
            if (report == null)
                return new NotFoundObjectResult("Expense report not found.");

            var blocked = ExpenseReportRules.MutateBlockedReason(report.Status);
            if (blocked != null)
                return new ConflictObjectResult(blocked);

            report.UpdatedAt = DateTime.UtcNow;

            var coverStart = ExpenseLineRules.CalendarDate(dto!.CoverStart);
            var coverEnd = ExpenseLineRules.CalendarDate(dto.CoverEnd);
            if (!ExpenseReportRules.IsValidCoverPeriod(coverStart, coverEnd))
                return new BadRequestObjectResult("Cover end date cannot be before cover start date.");

            report.CoverStart = coverStart;
            report.CoverEnd = coverEnd;
            report.ReportDate = coverEnd;

            var userPrefs = await _dbContext.UserSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId);
            report.AddressSnapshot = userPrefs?.ExpenseHomeAddress;
            report.Purpose = dto.Purpose;

            var codeError = await RejectUnknownCodesAsync(dto.Lines);
            if (codeError != null)
                return new BadRequestObjectResult(codeError);

            var currencies = await _dbContext.ExpenseCurrencies.AsNoTracking().ToListAsync();
            var policy = await LoadExpensePolicyAsync();
            if (policy.UserEntersRates)
            {
                foreach (var lineDto in dto.Lines)
                {
                    var (rateCategory, rateTransport) = ExpenseCategoryRules.ForStorage(
                        lineDto.Category, lineDto.TransportationCode);
                    if (ExpenseCategoryRules.IsPersonalCar(rateCategory, rateTransport))
                        continue;
                    var rateCode = ExpenseCurrencyRules.NormalizeCode(lineDto.CurrencyCode);
                    if (ExpenseCurrencyRules.IsHome(rateCode, policy.CompanyCurrencyCode))
                        continue;
                    if (lineDto.ExchangeRateToUsd <= 0m)
                        return new BadRequestObjectResult($"Enter the exchange rate for {rateCode}.");
                }
            }

            var chargeError = await ApplyChargeToAsync(report, dto.ChargeToOrganizationId, required: false);
            if (chargeError != null)
                return chargeError;

            var existing = report.Lines.ToDictionary(l => l.ExpenseLineId, StringComparer.Ordinal);
            var currencyError = RejectUnknownCurrency(dto.Lines, existing, currencies);
            if (currencyError != null)
                return new BadRequestObjectResult(currencyError);

            var keep = new HashSet<string>(StringComparer.Ordinal);
            var sort = 0;
            var receiptNamingChanged = false;
            foreach (var lineDto in dto!.Lines)
            {
                var (category, transportCode) = ExpenseCategoryRules.ForStorage(
                    lineDto.Category, lineDto.TransportationCode);
                var isPersonalCar = ExpenseCategoryRules.IsPersonalCar(category, transportCode);
                var isMeals = string.Equals(category, ExpenseCategoryRules.Meals, StringComparison.Ordinal);
                var isTransport = string.Equals(category, ExpenseCategoryRules.Transportation, StringComparison.Ordinal);
                var isMisc = string.Equals(category, ExpenseCategoryRules.Miscellaneous, StringComparison.Ordinal);

                ExpenseLine line;
                ExpenseLine? previous = null;
                var nextDate = ExpenseLineRules.CalendarDate(lineDto.Date);
                var nextDescription = lineDto.Description.Trim();
                if (!string.IsNullOrWhiteSpace(lineDto.ExpenseLineId)
                    && existing.TryGetValue(lineDto.ExpenseLineId, out var found))
                {
                    line = found;
                    previous = found;
                    if (line.Date != nextDate || !string.Equals(line.Description, nextDescription, StringComparison.Ordinal))
                        receiptNamingChanged = true;
                }
                else
                {
                    var newId = Guid.TryParse(lineDto.ExpenseLineId, out var parsed)
                        ? parsed.ToString("N")
                        : Guid.NewGuid().ToString("N");
                    line = new ExpenseLine
                    {
                        ExpenseLineId = newId,
                        ExpenseReportId = report.ExpenseReportId
                    };
                    report.Lines.Add(line);
                    receiptNamingChanged = true;
                }

                line.Date = nextDate;
                line.Description = nextDescription;
                line.Category = category;
                line.Miles = isPersonalCar ? lineDto.Miles : null;
                var spent = lineDto.SpentAmount;
                if (spent == 0m && lineDto.Amount > 0m && ExpenseCurrencyRules.IsUsd(lineDto.CurrencyCode))
                    spent = lineDto.Amount;
                var catalog = currencies.FirstOrDefault(row =>
                    string.Equals(row.Code, ExpenseCurrencyRules.NormalizeCode(lineDto.CurrencyCode), StringComparison.Ordinal));
                var money = ExpenseMoneyRules.Apply(
                    category,
                    transportCode,
                    lineDto.Miles,
                    report.MileageRateSnapshot,
                    spent,
                    lineDto.CurrencyCode,
                    catalog?.RateToUsd ?? 1m,
                    policy.UserEntersRates ? lineDto.ExchangeRateToUsd : previous?.ExchangeRateToUsd,
                    policy.UserEntersRates ? lineDto.CurrencyCode : previous?.CurrencyCode,
                    policy.CompanyCurrencyCode,
                    policy.UserEntersRates,
                    policy.UserEntersRates ? lineDto.ExchangeRateToUsd : null);
                line.CurrencyCode = money.CurrencyCode;
                line.ExchangeRateToUsd = money.ExchangeRateToUsd;
                line.SpentAmount = money.SpentAmount;
                line.Amount = money.AmountUsd;
                line.TransportationCode = isTransport ? transportCode : null;
                line.MiscellaneousCode = isMisc ? ExpenseCategoryRules.NormalizeCode(lineDto.MiscellaneousCode) : null;
                if (isMeals)
                {
                    var meal = ExpenseMealRules.Normalize(lineDto.MealType, lineDto.HeadCount);
                    line.MealType = meal.MealType;
                    line.HeadCount = meal.HeadCount;
                }
                else
                {
                    line.MealType = null;
                    line.HeadCount = null;
                }

                line.SortOrder = sort++;
                keep.Add(line.ExpenseLineId);
            }

            var creditError = SyncFormCredits(report, dto.FormCredits, currencies, policy);
            if (creditError != null)
                return new BadRequestObjectResult(creditError);

            var removed = report.Lines.Where(l => !keep.Contains(l.ExpenseLineId)).ToList();
            var removedDriveIds = DeleteLinesAndReceipts(removed);

            if (receiptNamingChanged)
                await RelocateExpenseReceiptsAsync(report);

            await _dbContext.SaveChangesAsync();
            await TryDeleteDriveFilesAsync(removedDriveIds);

            var reloaded = await LoadOwnedReportAsync(report.ExpenseReportId, userId, tracking: false);
            return new OkObjectResult(await ToDtoAsync(reloaded!));
        }

        [Function("DeleteExpenseReport")]
        public async Task<IActionResult> DeleteAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "expenses/reports/{id}")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth) return unauth;

            var report = await LoadOwnedReportAsync(id, userId, tracking: true);
            if (report == null)
                return new NotFoundObjectResult("Expense report not found.");

            var blocked = ExpenseReportRules.MutateBlockedReason(report.Status);
            if (blocked != null)
                return new ConflictObjectResult(blocked);

            var driveIds = report.Lines
                .SelectMany(l => (l.Receipts ?? []).Select(r => r.DriveFileId))
                .Concat(report.FiledPdfs.Select(file => file.DriveFileId))
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .ToList();

            _dbContext.ExpenseReports.Remove(report);
            await _dbContext.SaveChangesAsync();
            await TryDeleteDriveFilesAsync(driveIds);
            return new OkResult();
        }

        [Function("UploadExpenseReceipt")]
        public async Task<IActionResult> UploadReceiptAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses/reports/{reportId}/lines/{lineId}/receipts")] HttpRequestData req,
            string reportId,
            string lineId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth) return unauth;

            var report = await LoadOwnedReportAsync(reportId, userId, tracking: true);
            if (report == null)
                return new NotFoundObjectResult("Expense report not found.");

            var blocked = ExpenseReportRules.MutateBlockedReason(report.Status);
            if (blocked != null)
                return new ConflictObjectResult(blocked);

            var line = report.Lines.FirstOrDefault(l => l.ExpenseLineId == lineId);
            if (line == null)
                return new NotFoundObjectResult("Line not found.");

            if (!ExpenseReceiptRules.TryValidateCount(line.Receipts?.Count ?? 0, 1, out var countError))
                return new BadRequestObjectResult(countError);

            UploadExpenseReceiptDto? body;
            try
            {
                body = await System.Text.Json.JsonSerializer.DeserializeAsync<UploadExpenseReceiptDto>(
                    req.Body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return new BadRequestObjectResult("Invalid receipt payload.");
            }

            if (body == null || string.IsNullOrWhiteSpace(body.ContentBase64))
                return new BadRequestObjectResult("Receipt file is required.");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(body.ContentBase64);
            }
            catch
            {
                return new BadRequestObjectResult("Receipt file is not valid base64.");
            }

            if (!ExpenseReceiptRules.TryValidate(body.FileName, bytes.Length, out var error))
                return new BadRequestObjectResult(error);
            if (ExpensePacketReceiptRules.IsNestedExpensePacket(body.FileName, bytes))
                return new BadRequestObjectResult(ExpensePacketReceiptRules.RejectMessage);

            var fileName = body.FileName.Trim();
            var mime = string.IsNullOrWhiteSpace(body.MimeType) ? "application/octet-stream" : body.MimeType.Trim();
            if (ExpenseReceiptRules.IsHeic(fileName, mime))
            {
                if (!HeicJpegConverter.TryConvert(bytes, out var jpeg))
                    return new BadRequestObjectResult(
                        "Couldn't convert that HEIC photo. Try saving it as JPEG or PNG and upload again.");
                bytes = jpeg;
                fileName = Path.ChangeExtension(fileName, ".jpg");
                mime = "image/jpeg";
            }

            var driveAccess = await TryGetAppDriveAsync();
            if (driveAccess.Error != null)
                return driveAccess.Error;

            string? driveFileId = null;
            try
            {
                driveFileId = await UploadReceiptToDriveAsync(
                    driveAccess.Token!,
                    driveAccess.ExpensesFolderId!,
                    report,
                    line,
                    fileName,
                    mime,
                    bytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expense receipt Drive upload failed for report {ReportId}.", reportId);
                return new ObjectResult(ApiErrorMessages.DriveOperationFailed) { StatusCode = 502 };
            }

            var receipt = new ExpenseReceipt
            {
                ExpenseReceiptId = Guid.NewGuid().ToString("N"),
                ExpenseLineId = line.ExpenseLineId,
                OriginalFileName = fileName,
                MimeType = mime,
                SizeBytes = bytes.Length,
                Content = null,
                DriveFileId = driveFileId,
                UploadedAt = DateTime.UtcNow,
                UploadedByUserId = userId
            };
            _dbContext.ExpenseReceipts.Add(receipt);
            report.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            return new OkObjectResult(new ExpenseReceiptDto
            {
                ExpenseReceiptId = receipt.ExpenseReceiptId,
                OriginalFileName = receipt.OriginalFileName,
                MimeType = receipt.MimeType,
                SizeBytes = receipt.SizeBytes
            });
        }

        [Function("GetExpenseReceiptMedia")]
        public async Task<IActionResult> GetReceiptMediaAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/receipts/{receiptId}/media")] HttpRequestData req,
            string receiptId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth) return unauth;

            var receipt = await _dbContext.ExpenseReceipts
                .Include(r => r.ExpenseLine).ThenInclude(l => l.ExpenseReport)
                .FirstOrDefaultAsync(r => r.ExpenseReceiptId == receiptId);
            if (receipt == null
                || !ExpenseReportRules.CanView(receipt.ExpenseLine.ExpenseReport.UserId, userId, IsExpensesManager(principal)))
                return new NotFoundObjectResult("Receipt not found.");

            if (!string.IsNullOrEmpty(receipt.DriveFileId))
            {
                var driveAccess = await TryGetAppDriveAsync();
                if (driveAccess.Error != null)
                    return driveAccess.Error;
                try
                {
                    var (bytes, mime) = await _drive.DownloadFileContentAsync(
                        driveAccess.Token!, receipt.DriveFileId);
                    return new FileContentResult(bytes, mime)
                    {
                        FileDownloadName = receipt.OriginalFileName
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Expense receipt Drive download failed for {ReceiptId}.", receiptId);
                    return new ObjectResult(ApiErrorMessages.DriveOperationFailed) { StatusCode = 502 };
                }
            }

            if (receipt.Content == null || receipt.Content.Length == 0)
                return new NotFoundObjectResult("Receipt file is missing.");

            return new FileContentResult(receipt.Content, receipt.MimeType)
            {
                FileDownloadName = receipt.OriginalFileName
            };
        }

        [Function("DeleteExpenseLine")]
        public async Task<IActionResult> DeleteLineAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "expenses/reports/{reportId}/lines/{lineId}")] HttpRequestData req,
            string reportId,
            string lineId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth) return unauth;

            var report = await LoadOwnedReportAsync(reportId, userId, tracking: true);
            if (report == null)
                return new NotFoundObjectResult("Expense report not found.");

            var blocked = ExpenseReportRules.MutateBlockedReason(report.Status);
            if (blocked != null)
                return new ConflictObjectResult(blocked);

            var line = report.Lines.FirstOrDefault(l => l.ExpenseLineId == lineId);
            if (line == null)
                return new NotFoundObjectResult("Line not found.");

            var driveIds = DeleteLinesAndReceipts([line]);
            report.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            await TryDeleteDriveFilesAsync(driveIds);
            return new OkResult();
        }

        [Function("DeleteExpenseReceipt")]
        public async Task<IActionResult> DeleteReceiptAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "expenses/receipts/{receiptId}")] HttpRequestData req,
            string receiptId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth) return unauth;

            var receipt = await _dbContext.ExpenseReceipts
                .Include(r => r.ExpenseLine).ThenInclude(l => l.ExpenseReport)
                .FirstOrDefaultAsync(r => r.ExpenseReceiptId == receiptId);
            if (receipt == null || receipt.ExpenseLine.ExpenseReport.UserId != userId)
                return new NotFoundObjectResult("Receipt not found.");

            var blocked = ExpenseReportRules.MutateBlockedReason(receipt.ExpenseLine.ExpenseReport.Status);
            if (blocked != null)
                return new ConflictObjectResult(blocked);

            var driveFileId = receipt.DriveFileId;
            _dbContext.ExpenseReceipts.Remove(receipt);
            receipt.ExpenseLine.ExpenseReport.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            if (!string.IsNullOrEmpty(driveFileId))
                await TryDeleteDriveFilesAsync([driveFileId]);
            return new OkResult();
        }

        private async Task<ExpenseReport?> LoadOwnedReportAsync(string id, string userId, bool tracking)
        {
            var query = tracking
                ? _dbContext.ExpenseReports
                    .Include(r => r.Lines).ThenInclude(l => l.Receipts)
                    .Include(r => r.FormCredits)
                    .Include(r => r.Department)
                    .Include(r => r.FiledPdfs)
                    .AsQueryable()
                : _dbContext.ExpenseReports.AsNoTracking()
                    .Include(r => r.Lines).ThenInclude(l => l.Receipts)
                    .Include(r => r.FormCredits)
                    .Include(r => r.Department)
                    .Include(r => r.FiledPdfs);

            return await query
                .FirstOrDefaultAsync(r => r.ExpenseReportId == id && r.UserId == userId);
        }

        private async Task<ExpenseReport?> LoadReportAsync(string id, string userId, bool isManager, bool tracking)
        {
            var query = tracking
                ? _dbContext.ExpenseReports
                    .Include(r => r.Lines).Include(r => r.Department).Include(r => r.FiledPdfs).AsQueryable()
                : _dbContext.ExpenseReports.AsNoTracking()
                    .Include(r => r.Lines).Include(r => r.Department).Include(r => r.FiledPdfs);

            var report = await query.FirstOrDefaultAsync(r => r.ExpenseReportId == id);
            if (report == null || !ExpenseReportRules.CanView(report.UserId, userId, isManager))
                return null;
            return report;
        }

        private static bool IsExpensesManager(ClaimsPrincipal principal) =>
            Constants.Roles.HasScopedAccess(principal, Constants.Scopes.Expenses, Constants.Roles.Manager);

        private static IQueryable<ExpenseReport> FilterByStatus(IQueryable<ExpenseReport> query, string status)
        {
            var stored = ExpenseDataExtractionRules.StoredStatusForFilter(status);
            return stored == null ? query : query.Where(r => r.Status == stored);
        }

        /// <summary>
        /// A year or month filter is the submit month. Drafts are excluded because they have no filing month yet.
        /// </summary>
        private static IQueryable<ExpenseReport> FilterByFilingMonth(
            IQueryable<ExpenseReport> query,
            IReadOnlyCollection<int> years,
            IReadOnlyCollection<int> months)
        {
            if (years.Count == 0 && months.Count == 0)
                return query;

            query = query.Where(r =>
                r.Status == ExpenseStatusRules.Submitted || r.Status == ExpenseStatusRules.Reimbursed);
            if (years.Count > 0)
                query = query.Where(r => years.Contains(r.Year));
            if (months.Count > 0)
                query = query.Where(r => months.Contains(r.Month));
            return query;
        }

        private async Task<List<ExpenseReportListDto>> LoadListAsync(IQueryable<ExpenseReport> source)
        {
            var reports = await source
                .Include(r => r.Lines)
                .ThenInclude(l => l.Receipts)
                .ToListAsync();
            var zones = await OwnerTimeZonesAsync(reports.Select(report => report.UserId));
            var rows = reports.Select(report => ToListDto(report, ZoneFor(zones, report.UserId))).ToList();
            foreach (var row in rows)
                row.GrossAmount = row.TotalAmount;
            var policy = await LoadExpensePolicyAsync();
            if (!policy.CreditBucketEnabled || rows.Count == 0)
                return rows;

            var lineCredits = !ExpenseCreditEntryRules.IsOnForm(policy.CreditEntryMode);
            var reportIds = rows.Select(row => row.ExpenseReportId).ToList();
            var formRows = await _dbContext.ExpenseCredits.AsNoTracking()
                .Where(credit => reportIds.Contains(credit.ExpenseReportId))
                .Select(credit => new
                {
                    credit.ExpenseReportId,
                    credit.Amount,
                    credit.CurrencyCode,
                    credit.ExchangeRateToUsd
                })
                .ToListAsync();
            var pools = lineCredits
                ? await LoadCreditPoolsAsync(reports.Select(report => report.UserId).ToList())
                : [];
            foreach (var row in rows)
            {
                var net = row.TotalAmount;
                if (lineCredits && pools.TryGetValue(row.UserId, out var pool))
                {
                    net = ExpenseReportMoneyRules.NetForReport(
                        row.ExpenseReportId,
                        pool.Sources,
                        pool.Lines,
                        creditsEnabled: true,
                        homeCode: policy.CompanyCurrencyCode);
                }

                var extra = formRows
                    .Where(credit => credit.ExpenseReportId == row.ExpenseReportId)
                    .Sum(credit => ExpenseCreditEntryRules.HomeAmount(
                        credit.Amount, credit.CurrencyCode, credit.ExchangeRateToUsd, policy.CompanyCurrencyCode));
                if (extra > 0m)
                    net -= extra;
                row.TotalAmount = net < 0m ? 0m : net;
            }

            return rows;
        }

        private static string? ZoneFor(IReadOnlyDictionary<string, string?> zones, string? userId)
        {
            if (string.IsNullOrEmpty(userId))
                return null;
            return zones.TryGetValue(userId, out var zone) ? zone : null;
        }

        private static ExpenseReportListDto ToListDto(ExpenseReport report, string? timeZoneId)
        {
            var lines = report.Lines.OrderBy(l => l.SortOrder).ToList();
            return new ExpenseReportListDto
            {
                ExpenseReportId = report.ExpenseReportId,
                UserId = report.UserId,
                EmployeeName = report.EmployeeNameSnapshot ?? "",
                Year = report.Year,
                Month = report.Month,
                CoverStart = report.CoverStart,
                CoverEnd = report.CoverEnd,
                ReportDate = report.ReportDate,
                Status = report.Status,
                LineCount = lines.Count,
                TotalAmount = lines.Sum(l => l.Amount),
                SubmittedAt = report.SubmittedAt,
                ReimbursedAt = report.ReimbursedAt,
                HasPaymentDate = report.ReimbursedAt != null,
                PaymentDate = report.ReimbursedAt == null
                    ? default
                    : ExpensePaymentDateRules.ToCalendarDate(report.ReimbursedAt.Value, timeZoneId),
                UpdatedAt = report.UpdatedAt,
                Purpose = report.Purpose,
                PlantOrLocation = report.PlantOrLocation,
                ChargeToNote = report.ChargeToNote,
                ChargeToOrganizationId = report.ChargeToOrganizationId,
                ChargeToName = report.ChargeToName,
                Lines = lines.Select(l => new ExpenseLineSearchDto
                {
                    Date = l.Date,
                    Description = l.Description,
                    Category = l.Category,
                    Amount = l.Amount,
                    Miles = l.Miles,
                    TransportationCode = l.TransportationCode,
                    MiscellaneousCode = l.MiscellaneousCode,
                    MealType = l.MealType,
                    HeadCount = l.HeadCount,
                    ReceiptFileNames = l.Receipts.Select(r => r.OriginalFileName).ToList()
                }).ToList()
            };
        }

        private static ExpenseDataExportDto BuildExport(IReadOnlyList<ExpenseReport> reports, IReadOnlyCollection<string> entities)
        {
            var export = new ExpenseDataExportDto();
            var includeReports = entities.Contains(ExpenseDataExtractionRules.Reports, StringComparer.OrdinalIgnoreCase);
            var includeLines = entities.Contains(ExpenseDataExtractionRules.Lines, StringComparer.OrdinalIgnoreCase);
            var includeReceipts = entities.Contains(ExpenseDataExtractionRules.Receipts, StringComparer.OrdinalIgnoreCase);

            foreach (var report in reports)
            {
                var employee = report.EmployeeNameSnapshot ?? "";
                var lines = report.Lines.OrderBy(l => l.SortOrder).ToList();
                if (includeReports)
                {
                    export.Reports.Add(new ExpenseReportExportRow
                    {
                        ExpenseReportId = report.ExpenseReportId,
                        UserId = report.UserId,
                        EmployeeName = employee,
                        Year = report.Year,
                        Month = report.Month,
                        CoverStart = report.CoverStart,
                        CoverEnd = report.CoverEnd,
                        Status = report.Status,
                        SubmittedAt = report.SubmittedAt,
                        ReimbursedAt = report.ReimbursedAt,
                        LineCount = lines.Count,
                        TotalAmount = lines.Sum(l => l.Amount),
                        MileageRateSnapshot = report.MileageRateSnapshot,
                        ChargeToOrganizationId = report.ChargeToOrganizationId,
                        ChargeToName = report.ChargeToName
                    });
                }

                foreach (var line in lines)
                {
                    var (category, transport) = ExpenseCategoryRules.ForStorage(line.Category, line.TransportationCode);
                    if (includeLines)
                    {
                        export.Lines.Add(new ExpenseLineExportRow
                        {
                            ExpenseLineId = line.ExpenseLineId,
                            ExpenseReportId = report.ExpenseReportId,
                            EmployeeName = employee,
                            Year = report.Year,
                            Month = report.Month,
                            Status = report.Status,
                            Date = ExpenseLineRules.CalendarDate(line.Date),
                            Description = line.Description,
                            Category = category,
                            Amount = line.Amount,
                            SpentAmount = line.SpentAmount,
                            CurrencyCode = string.IsNullOrWhiteSpace(line.CurrencyCode)
                                ? ExpenseCurrencyRules.UsdCode
                                : line.CurrencyCode,
                            ExchangeRateToUsd = line.ExchangeRateToUsd <= 0m ? 1m : line.ExchangeRateToUsd,
                            IsReturned = line.ReturnedAt != null,
                            ReturnedOn = line.ReturnedAt == null
                                ? default
                                : ExpensePaymentDateRules.ToCalendarDate(line.ReturnedAt.Value, null),
                            Miles = line.Miles,
                            TransportationCode = transport,
                            MiscellaneousCode = line.MiscellaneousCode,
                            MealType = line.MealType,
                            HeadCount = line.HeadCount,
                            ReceiptCount = line.Receipts?.Count ?? 0
                        });
                    }

                    if (includeReceipts && line.Receipts != null)
                    {
                        foreach (var receipt in line.Receipts.OrderBy(r => r.UploadedAt))
                        {
                            export.Receipts.Add(new ExpenseReceiptExportRow
                            {
                                ExpenseReceiptId = receipt.ExpenseReceiptId,
                                ExpenseLineId = line.ExpenseLineId,
                                ExpenseReportId = report.ExpenseReportId,
                                EmployeeName = employee,
                                OriginalFileName = receipt.OriginalFileName,
                                MimeType = receipt.MimeType,
                                SizeBytes = receipt.SizeBytes,
                                UploadedAt = receipt.UploadedAt
                            });
                        }
                    }
                }
            }

            return export;
        }

        private async Task<ExpenseSettingsDto> LoadExpenseSettingsDtoAsync()
        {
            var policy = await LoadExpensePolicyAsync();
            var settings = await LoadExpenseSettingsAsync();
            var (sig, _) = await LoadApprovalSignatureAsync();
            var codes = await LoadCodesAsync();
            var notifyIds = await LoadNotifyUserIdsAsync();
            var notifyUsers = notifyIds.Count == 0
                ? []
                : await _dbContext.ApplicationUsers.AsNoTracking()
                    .Where(user => notifyIds.Contains(user.Id) && user.IsActive && !user.IsArchived)
                    .Select(user => new ExpenseNotifyUserDto
                    {
                        UserId = user.Id,
                        Name = (user.FirstName + " " + user.LastName).Trim(),
                        Email = user.Email ?? ""
                    })
                    .ToListAsync();
            notifyUsers = notifyIds
                .Select(id => notifyUsers.FirstOrDefault(user => user.UserId == id))
                .Where(user => user != null)
                .Cast<ExpenseNotifyUserDto>()
                .ToList();

            return new ExpenseSettingsDto
            {
                MileageRatePerMile = settings.MileageRate,
                HasApprovalSignature = sig is { Length: > 0 },
                MiscellaneousCodes = codes
                    .Where(row => row.Kind == ExpenseCodeKindRules.Miscellaneous && !row.IsRemoved)
                    .Select(ToCodeDto)
                    .ToList(),
                TransportationCodes = codes
                    .Where(row => row.Kind == ExpenseCodeKindRules.Transportation)
                    .Select(ToCodeDto)
                    .ToList(),
                NotifyUsers = notifyUsers,
                Currencies = (await _dbContext.ExpenseCurrencies.AsNoTracking()
                        .Where(row => !row.IsRemoved)
                        .OrderBy(row => row.SortOrder)
                        .ThenBy(row => row.Code)
                        .ToListAsync())
                    .Select(ToCurrencyDto)
                    .ToList(),
                CreditBucketEnabled = policy.CreditBucketEnabled,
                CreditEntryMode = policy.CreditEntryMode,
                UserEntersRates = policy.UserEntersRates,
                CompanyCurrencyCode = policy.CompanyCurrencyCode
            };
        }

        private static ExpenseCodeDto ToCodeDto(ExpenseCode row) => new()
        {
            ExpenseCodeId = row.ExpenseCodeId,
            Kind = row.Kind,
            Code = row.Code,
            Label = row.Label,
            IsActive = row.IsActive,
            IsLocked = row.IsLocked,
            SortOrder = row.SortOrder
        };

        private async Task<(byte[] Bytes, string FileName, string ContentType)> BuildReportPdfAsync(
            ExpenseReport report, bool chargeSpecified, string chargeKey)
        {
            var packets = await BuildPacketsAsync(report);
            var packet = !chargeSpecified
                ? packets.Count == 1
                    ? packets[0]
                    : throw new InvalidOperationException(
                        "This report has more than one Charge to. Open each form from the PDF menu.")
                : packets.FirstOrDefault(item => string.Equals(item.ChargeKey, chargeKey, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("That Charge to is not on this expense.");
            return (packet.Bytes, packet.FileName, "application/pdf");
        }

        private async Task<List<ExpensePacket>> BuildPacketsAsync(ExpenseReport report)
        {
            var settings = await LoadExpenseSettingsAsync();
            string? homeName = null;
            if (!string.IsNullOrEmpty(settings.HomeOrganizationId))
            {
                var org = await _dbContext.Organizations.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.OrganizationId == settings.HomeOrganizationId);
                homeName = org?.Name;
            }

            var fallback = !string.IsNullOrWhiteSpace(report.ChargeToNote)
                ? report.ChargeToNote.Trim()
                : homeName ?? "Home Company";
            var chargeName = !string.IsNullOrWhiteSpace(report.ChargeToName)
                ? report.ChargeToName.Trim()
                : fallback;
            var lines = report.Lines.OrderBy(line => line.SortOrder).ToList();

            var ownerSettings = await _dbContext.UserSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == report.UserId);
            var (managerSig, _) = await LoadApprovalSignatureAsync();
            var coverStart = ExpenseLineRules.CalendarDate(report.CoverStart);
            var coverEnd = ExpenseLineRules.CalendarDate(report.CoverEnd);
            var reportDate = ExpenseLineRules.CalendarDate(report.ReportDate);
            var policy = await LoadExpensePolicyAsync();
            var lineCredits = policy.CreditBucketEnabled
                && !ExpenseCreditEntryRules.IsOnForm(policy.CreditEntryMode);
            IReadOnlyDictionary<string, decimal>? applied = null;
            if (lineCredits)
            {
                var pools = await LoadCreditPoolsAsync([report.UserId]);
                if (pools.TryGetValue(report.UserId, out var pool))
                    applied = ExpenseCreditRules.AppliedByLine(pool.Sources, pool.Lines, policy.CompanyCurrencyCode);
            }

            var formCreditRows = policy.CreditBucketEnabled
                ? await _dbContext.ExpenseCredits.AsNoTracking()
                    .Where(credit => credit.ExpenseReportId == report.ExpenseReportId)
                    .ToListAsync()
                : [];
            var formCreditUsd = formCreditRows.Sum(credit => ExpenseCreditEntryRules.HomeAmount(
                credit.Amount, credit.CurrencyCode, credit.ExchangeRateToUsd, policy.CompanyCurrencyCode));

            var fileName = ExpenseDriveNamingRules.StatementFileName(
                coverStart, coverEnd, report.EmployeeNameSnapshot);
            var home = policy.CompanyCurrencyCode;
            var homeLabel = "US dollars";
            if (!ExpenseCurrencyRules.IsUsd(home))
            {
                var currencyName = await _dbContext.ExpenseCurrencies.AsNoTracking()
                    .Where(row => row.Code == home)
                    .Select(row => row.Name)
                    .FirstOrDefaultAsync();
                homeLabel = string.IsNullOrWhiteSpace(currencyName) ? home : currencyName;
            }
            var groups = lines
                .GroupBy(line => ExpenseCurrencyRules.NormalizeCode(line.CurrencyCode))
                .OrderBy(group => ExpenseCurrencyRules.IsHome(group.Key, home) ? 0 : 1)
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.OrderBy(line => line.SortOrder).ToList())
                .ToList();
            if (groups.Count == 0)
                groups.Add([]);
            var homeIndex = groups.FindIndex(group =>
                group.Count > 0 && ExpenseCurrencyRules.IsHome(group[0].CurrencyCode, home));
            if (homeIndex < 0)
                homeIndex = 0;

            var parts = new List<byte[]>();
            for (var index = 0; index < groups.Count; index++)
            {
                var groupLines = groups[index];
                var mapped = groupLines.Select(line =>
                {
                    var (category, transport) = ExpenseCategoryRules.ForStorage(line.Category, line.TransportationCode);
                    var entered = ExpenseMoneyRules.EnteredAmount(line.SpentAmount, line.Amount, line.CurrencyCode);
                    return ExpenseForm8743Rules.MapLine(
                        line.Date, line.Description, category, entered, line.Miles, transport,
                        line.MiscellaneousCode, line.MealType, line.HeadCount);
                }).ToList();
                var usd = groupLines.Sum(line => line.Amount);
                decimal credit = 0m;
                if (applied != null)
                {
                    foreach (var line in groupLines)
                    {
                        if (applied.TryGetValue(line.ExpenseLineId, out var part))
                            credit += part;
                    }
                }

                if (policy.CreditBucketEnabled && index == homeIndex)
                    credit += formCreditUsd;
                if (credit > usd)
                    credit = usd;
                var code = groupLines.Count == 0
                    ? home
                    : ExpenseCurrencyRules.NormalizeCode(groupLines[0].CurrencyCode);
                var model = new ExpenseForm8743Model
                {
                    CompanyName = string.IsNullOrWhiteSpace(homeName) ? "Home Company" : homeName,
                    EmployeeName = report.EmployeeNameSnapshot ?? "",
                    PlantOrLocation = string.IsNullOrWhiteSpace(report.PlantOrLocation)
                        ? ExpenseForm8743Rules.DefaultPlant
                        : report.PlantOrLocation,
                    ChargeTo = chargeName,
                    Address = string.IsNullOrWhiteSpace(report.AddressSnapshot)
                        ? ownerSettings?.ExpenseHomeAddress
                        : report.AddressSnapshot,
                    CoverStart = coverStart,
                    CoverEnd = coverEnd,
                    ReportDate = reportDate,
                    MileageRate = report.MileageRateSnapshot,
                    Purpose = report.Purpose,
                    Lines = mapped,
                    CurrencyRows =
                    [
                        new ExpenseFormCurrencyRow
                        {
                            Code = code,
                            Spent = groupLines.Sum(line => ExpenseMoneyRules.EnteredAmount(
                                line.SpentAmount, line.Amount, line.CurrencyCode)),
                            Usd = usd
                        }
                    ],
                    CreditUsd = credit,
                    NetReimbursement = usd - credit,
                    HasCurrencyFooter = true,
                    EmployeeSignature = ownerSettings?.ExpenseSignature,
                    ManagerSignature = managerSig,
                    FileName = fileName,
                    HomeCurrencyLabel = homeLabel
                };

                var receiptPages = new List<ExpenseReceiptPdfPage>();
                foreach (var line in groupLines)
                {
                    foreach (var receipt in (line.Receipts ?? []).OrderBy(r => r.UploadedAt))
                    {
                        var bytes = await ReadReceiptBytesAsync(receipt);
                        if (bytes == null || bytes.Length == 0)
                            continue;
                        receiptPages.Add(new ExpenseReceiptPdfPage
                        {
                            FileName = receipt.OriginalFileName,
                            MimeType = receipt.MimeType,
                            Bytes = bytes
                        });
                    }
                }

                parts.Add(_pdf.BuildPacket(model, receiptPages));
            }

            return
            [
                new ExpensePacket
                {
                    Bytes = _pdf.Combine(parts),
                    FileName = fileName,
                    ChargeKey = ExpenseChargeToRules.Key(report.ChargeToOrganizationId),
                    ChargeName = chargeName
                }
            ];
        }

        private async Task<string?> TrySendSubmitEmailAsync(ExpenseReport report, IReadOnlyList<ExpensePacket> packets)
        {
            try
            {
                var notifyIds = await LoadNotifyUserIdsAsync();
                var recipients = notifyIds.Count == 0
                    ? []
                    : await _dbContext.ApplicationUsers.AsNoTracking()
                        .Where(user => notifyIds.Contains(user.Id) && user.IsActive && !user.IsArchived)
                        .Select(user => new { user.Id, user.Email })
                        .ToListAsync();
                var to = notifyIds
                    .Select(id => recipients.FirstOrDefault(user => user.Id == id)?.Email)
                    .Where(email => !string.IsNullOrWhiteSpace(email))
                    .Select(email => email!)
                    .ToList();

                var owner = await _dbContext.ApplicationUsers.AsNoTracking()
                    .Where(user => user.Id == report.UserId)
                    .Select(user => user.Email)
                    .FirstOrDefaultAsync();
                if (to.Count == 0 && !string.IsNullOrWhiteSpace(owner))
                    to.Add(owner);

                var cover = ExpenseReportRules.CoverRangeLabel(report.CoverStart, report.CoverEnd);
                var who = string.IsNullOrWhiteSpace(report.EmployeeNameSnapshot)
                    ? "An employee"
                    : report.EmployeeNameSnapshot.Trim();
                var link = ExpensePublicLinkRules.ReportUrl(
                    Environment.GetEnvironmentVariable("PublicBaseUrl"),
                    report.ExpenseReportId);
                var body =
                    $"{who} submitted an expense report covering {cover}.\n\n" +
                    $"Open it online: {link}\n";
                return await _mailer.SendAsync(
                    to,
                    owner,
                    $"Expense report — {who} — {cover}",
                    body,
                    packets.Select(packet => (packet.FileName, packet.Bytes)).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Expense submission email failed for {ReportId}.", report.ExpenseReportId);
                return "The report was submitted, but the email did not send.";
            }
        }

        private sealed class ExpensePacket
        {
            public byte[] Bytes { get; init; } = [];
            public string FileName { get; init; } = "";
            public string ChargeKey { get; init; } = "";
            public string ChargeName { get; init; } = "";
            public string? DriveFileId { get; set; }
        }

        private async Task<byte[]?> ReadReceiptBytesAsync(ExpenseReceipt receipt)
        {
            if (!string.IsNullOrEmpty(receipt.DriveFileId))
            {
                var driveAccess = await TryGetAppDriveAsync();
                if (driveAccess.Token == null)
                    throw new InvalidOperationException(
                        $"Could not reach Google Drive to fetch receipt {receipt.OriginalFileName}.");

                var (bytes, _) = await _drive.DownloadFileContentAsync(driveAccess.Token, receipt.DriveFileId);
                return bytes;
            }

            return receipt.Content;
        }

        private async Task<(byte[]? Bytes, string? Mime)> LoadApprovalSignatureAsync()
        {
            var keys = new[]
            {
                Constants.SettingKeys.ExpensesApprovalSignature,
                Constants.SettingKeys.ExpensesApprovalSignatureMime
            };
            var rows = await _dbContext.AppSettings.AsNoTracking()
                .Where(s => keys.Contains(s.Key))
                .Select(s => new { s.Key, s.Value })
                .ToListAsync();
            var raw = rows.FirstOrDefault(r => r.Key == Constants.SettingKeys.ExpensesApprovalSignature)?.Value;
            var mime = rows.FirstOrDefault(r => r.Key == Constants.SettingKeys.ExpensesApprovalSignatureMime)?.Value;
            if (string.IsNullOrWhiteSpace(raw))
                return (null, null);
            try
            {
                return (Convert.FromBase64String(raw), string.IsNullOrWhiteSpace(mime) ? "image/png" : mime);
            }
            catch
            {
                return (null, null);
            }
        }

        private static async Task<(byte[]? Bytes, string? Mime, IActionResult? Error)> ReadSignaturePayloadAsync(HttpRequestData req)
        {
            UploadExpenseSignatureDto? body;
            try
            {
                body = await System.Text.Json.JsonSerializer.DeserializeAsync<UploadExpenseSignatureDto>(
                    req.Body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return (null, null, new BadRequestObjectResult("Invalid signature payload."));
            }

            if (body == null || string.IsNullOrWhiteSpace(body.ContentBase64))
                return (null, null, new BadRequestObjectResult("Signature image is required."));

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(body.ContentBase64);
            }
            catch
            {
                return (null, null, new BadRequestObjectResult("Signature is not valid base64."));
            }

            var mime = ExpenseSignatureRules.NormalizeMime(body.MimeType, body.FileName);
            if (!ExpenseSignatureRules.TryValidate(body.FileName, mime, bytes.Length, out var error))
                return (null, null, new BadRequestObjectResult(error));

            return (bytes, mime, null);
        }

        private async Task UpsertSettingAsync(string key, string value, string description)
        {
            var row = await _dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (row == null)
            {
                _dbContext.AppSettings.Add(new AppSetting { Key = key, Value = value, Description = description });
                return;
            }

            row.Value = value;
            if (string.IsNullOrEmpty(row.Description))
                row.Description = description;
        }

        private async Task<(string? HomeOrganizationId, decimal MileageRate)> LoadExpenseSettingsAsync()
        {
            var keys = new[]
            {
                Constants.SettingKeys.HomeOrganizationId,
                Constants.SettingKeys.ExpensesMileageRatePerMile
            };
            var rows = await _dbContext.AppSettings.AsNoTracking()
                .Where(s => keys.Contains(s.Key))
                .Select(s => new { s.Key, s.Value })
                .ToListAsync();

            var home = rows.FirstOrDefault(r => r.Key == Constants.SettingKeys.HomeOrganizationId)?.Value;
            var rateRaw = rows.FirstOrDefault(r => r.Key == Constants.SettingKeys.ExpensesMileageRatePerMile)?.Value;
            return (
                string.IsNullOrWhiteSpace(home) ? null : home.Trim(),
                ExpenseMileageRateRules.Parse(rateRaw));
        }

        private async Task<ExpenseReportDto> ToDtoAsync(ExpenseReport report)
        {
            var lineIds = report.Lines.Select(l => l.ExpenseLineId).ToList();
            var receiptRows = lineIds.Count == 0
                ? []
                : await _dbContext.ExpenseReceipts.AsNoTracking()
                    .Where(r => lineIds.Contains(r.ExpenseLineId))
                    .Select(r => new
                    {
                        r.ExpenseLineId,
                        r.ExpenseReceiptId,
                        r.OriginalFileName,
                        r.MimeType,
                        r.SizeBytes
                    })
                    .ToListAsync();
            var grouped = receiptRows
                .GroupBy(r => r.ExpenseLineId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => new ExpenseReceiptDto
                    {
                        ExpenseReceiptId = x.ExpenseReceiptId,
                        OriginalFileName = x.OriginalFileName,
                        MimeType = x.MimeType,
                        SizeBytes = x.SizeBytes
                    }).ToList(),
                    StringComparer.Ordinal);

            var zone = await OwnerTimeZoneAsync(report.UserId);
            var policy = await LoadExpensePolicyAsync();
            var lineCredits = policy.CreditBucketEnabled
                && !ExpenseCreditEntryRules.IsOnForm(policy.CreditEntryMode);
            ExpenseCreditPool? pool = null;
            IReadOnlyDictionary<string, decimal> applied = new Dictionary<string, decimal>(StringComparer.Ordinal);
            if (lineCredits)
            {
                var pools = await LoadCreditPoolsAsync([report.UserId]);
                if (pools.TryGetValue(report.UserId, out var loaded))
                {
                    pool = loaded;
                    applied = ExpenseCreditRules.AppliedByLine(
                        loaded.Sources, loaded.Lines, policy.CompanyCurrencyCode);
                }
            }

            var formCreditRows = await _dbContext.ExpenseCredits.AsNoTracking()
                .Where(credit => credit.ExpenseReportId == report.ExpenseReportId)
                .OrderBy(credit => credit.SortOrder)
                .ToListAsync();

            var lines = report.Lines
                .OrderBy(l => l.SortOrder)
                .Select(l =>
                {
                    var (category, transport) = ExpenseCategoryRules.ForStorage(l.Category, l.TransportationCode);
                    return new ExpenseLineDto
                    {
                        ExpenseLineId = l.ExpenseLineId,
                        Date = ExpenseLineRules.CalendarDate(l.Date),
                        Description = l.Description,
                        Category = category,
                        Amount = l.Amount,
                        SpentAmount = l.SpentAmount,
                        CurrencyCode = ExpenseCurrencyRules.NormalizeCode(l.CurrencyCode),
                        ExchangeRateToUsd = l.ExchangeRateToUsd <= 0m ? 1m : l.ExchangeRateToUsd,
                        IsReturned = l.ReturnedAt != null,
                        ReturnedOn = l.ReturnedAt == null
                            ? default
                            : ExpensePaymentDateRules.ToCalendarDate(l.ReturnedAt.Value, zone),
                        CreditAmount = l.CreditAmount,
                        CreditCurrencyCode = l.CreditCurrencyCode,
                        CreditDescription = l.CreditDescription,
                        Miles = l.Miles,
                        TransportationCode = transport,
                        MiscellaneousCode = l.MiscellaneousCode,
                        MealType = l.MealType,
                        HeadCount = l.HeadCount,
                        SortOrder = l.SortOrder,
                        Receipts = grouped.TryGetValue(l.ExpenseLineId, out var list) ? list : []
                    };
                })
                .ToList();

            var gross = lines.Sum(l => l.Amount);
            decimal credit = 0m;
            if (lineCredits)
            {
                foreach (var line in lines)
                {
                    if (!string.IsNullOrEmpty(line.ExpenseLineId)
                        && applied.TryGetValue(line.ExpenseLineId, out var part))
                        credit += part;
                }
            }

            if (policy.CreditBucketEnabled)
            {
                credit += formCreditRows.Sum(row => ExpenseCreditEntryRules.HomeAmount(
                    row.Amount, row.CurrencyCode, row.ExchangeRateToUsd, policy.CompanyCurrencyCode));
            }

            if (credit > gross)
                credit = gross;

            return new ExpenseReportDto
            {
                ExpenseReportId = report.ExpenseReportId,
                UserId = report.UserId,
                Year = report.Year,
                Month = report.Month,
                CoverStart = report.CoverStart,
                CoverEnd = report.CoverEnd,
                ReportDate = report.ReportDate,
                Status = report.Status,
                SubmittedAt = report.SubmittedAt,
                ReimbursedAt = report.ReimbursedAt,
                Purpose = report.Purpose,
                PlantOrLocation = report.PlantOrLocation,
                DepartmentId = report.DepartmentId,
                DepartmentName = report.Department?.Name,
                ChargeToNote = report.ChargeToNote,
                ChargeToOrganizationId = report.ChargeToOrganizationId,
                ChargeToName = report.ChargeToName,
                EmployeeNameSnapshot = report.EmployeeNameSnapshot,
                AddressSnapshot = report.AddressSnapshot,
                MileageRateSnapshot = report.MileageRateSnapshot,
                CreatedAt = report.CreatedAt,
                UpdatedAt = report.UpdatedAt,
                Lines = lines,
                FormCredits = formCreditRows.Select(row => new ExpenseFormCreditDto
                {
                    ExpenseCreditId = row.ExpenseCreditId,
                    Date = ExpenseLineRules.CalendarDate(row.CreditDate),
                    Description = row.Description,
                    Amount = row.Amount,
                    CurrencyCode = row.CurrencyCode,
                    ExchangeRateToUsd = row.ExchangeRateToUsd
                }).ToList(),
                TotalAmount = gross - credit,
                GrossAmount = gross,
                CreditAmount = credit,
                CreditBucketEnabled = policy.CreditBucketEnabled,
                CreditEntryMode = policy.CreditEntryMode,
                UserEntersRates = policy.UserEntersRates,
                CompanyCurrencyCode = policy.CompanyCurrencyCode,
                HasPaymentDate = report.ReimbursedAt != null,
                PaymentDate = report.ReimbursedAt == null
                    ? default
                    : ExpensePaymentDateRules.ToCalendarDate(report.ReimbursedAt.Value, zone),
                CreditSources = pool == null
                    ? []
                    : pool.Sources.Select(source => new ExpenseCreditSourceDto
                    {
                        LineId = source.LineId,
                        ReportId = source.ReportId,
                        ChargeToOrganizationId = source.ChargeToOrganizationId,
                        ReturnDate = source.ReturnDate,
                        CurrencyCode = source.CurrencyCode,
                        SpentAmount = source.SpentAmount,
                        ExchangeRateToUsd = source.ExchangeRateToUsd,
                        AmountUsd = source.AmountUsd
                    }).ToList(),
                OtherCreditLines = pool == null
                    ? []
                    : pool.Lines
                        .Where(line => !string.Equals(line.ReportId, report.ExpenseReportId, StringComparison.Ordinal))
                        .Select(line => new ExpenseCreditLineDto
                        {
                            LineId = line.LineId,
                            ReportId = line.ReportId,
                            ChargeToOrganizationId = line.ChargeToOrganizationId,
                            ReportStatus = line.ReportStatus,
                            Date = line.Date,
                            CurrencyCode = line.CurrencyCode,
                            SpentAmount = line.SpentAmount,
                            ExchangeRateToUsd = line.ExchangeRateToUsd,
                            AmountUsd = line.AmountUsd,
                            SortOrder = line.SortOrder,
                            IsReturned = line.IsReturned
                        })
                        .ToList()
            };
        }

        private async Task<(string? Token, string? ExpensesFolderId, IActionResult? Error)> TryGetAppDriveAsync()
        {
            var cred = await _dbContext.AppDriveCredentials.AsNoTracking()
                .FirstOrDefaultAsync(c => c.AppDriveCredentialId == AppDriveLayoutRules.CredentialId);
            if (cred == null || string.IsNullOrEmpty(cred.EncryptedRefreshToken))
                return (null, null, new ObjectResult(AppDriveLayoutRules.NotConnectedMessage) { StatusCode = 409 });

            var folder = await _dbContext.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == Constants.SettingKeys.ExpensesDriveParentFolderId);
            var folderId = string.IsNullOrWhiteSpace(folder?.Value) ? null : folder.Value.Trim();
            if (string.IsNullOrEmpty(folderId))
                return (null, null, new ObjectResult(
                    "Expenses folder is missing. Open App Settings and click Ensure folders.") { StatusCode = 409 });

            return (cred.EncryptedRefreshToken, folderId, null);
        }

        private async Task<string> UploadFiledPacketAsync(ExpenseReport report, byte[] bytes, string fileName, string? previousFileId)
        {
            var access = await TryGetAppDriveAsync();
            if (access.Token == null || access.ExpensesFolderId == null)
                throw new InvalidOperationException(
                    access.Error is ObjectResult obj && obj.Value is string text && !string.IsNullOrWhiteSpace(text)
                        ? text
                        : AppDriveLayoutRules.NotConnectedMessage);

            var filedRoot = await _drive.FindOrCreateFolderAsync(
                access.Token, access.ExpensesFolderId, ExpenseDriveNamingRules.FiledFolderName);
            if (string.IsNullOrEmpty(filedRoot.Id))
                throw new InvalidOperationException("Couldn't create the Expenses Filed folder.");

            var periodFolder = await _drive.FindOrCreateFolderAsync(
                access.Token, filedRoot.Id, ExpenseDriveNamingRules.PeriodFolderName(report.Year, report.Month));
            if (string.IsNullOrEmpty(periodFolder.Id))
                throw new InvalidOperationException("Couldn't create the Filed month folder.");

            var existing = await _drive.FindChildByNameAsync(access.Token, periodFolder.Id, fileName);
            // Leave the previously filed file until SaveChanges succeeds. A same-named
            // file that is not that previous file is an orphan and can go now.
            if (existing != null
                && !string.Equals(existing.Id, previousFileId, StringComparison.Ordinal))
                await _drive.DeleteFileAsync(access.Token, existing.Id);

            using var stream = new MemoryStream(bytes);
            var uploaded = await _drive.UploadFileAsync(
                access.Token, stream, fileName, "application/pdf", periodFolder.Id);
            if (string.IsNullOrEmpty(uploaded.Id))
                throw new InvalidOperationException("Drive upload returned no file id for the filed expense PDF.");
            return uploaded.Id;
        }

        private async Task<(byte[] Bytes, string FileName, string ContentType)?> TryDownloadFiledPacketAsync(
            ExpenseReport report, bool chargeSpecified, string chargeKey)
        {
            var files = report.FiledPdfs.OrderBy(file => file.SortOrder).ToList();
            if (files.Count == 0)
                return null;

            ExpenseFiledPdf? match;
            if (!chargeSpecified)
            {
                if (files.Count != 1)
                    return null;
                match = files[0];
            }
            else
            {
                match = files.FirstOrDefault(file =>
                    string.Equals(ExpenseChargeToRules.Key(file.ChargeToOrganizationId), chargeKey, StringComparison.Ordinal));
            }

            if (match == null || string.IsNullOrEmpty(match.DriveFileId))
                return null;

            var access = await TryGetAppDriveAsync();
            if (access.Token == null)
                return null;

            try
            {
                var (bytes, mime) = await _drive.DownloadFileContentAsync(access.Token, match.DriveFileId);
                var fileName = ExpenseDriveNamingRules.StatementFileName(
                    report.CoverStart, report.CoverEnd, report.EmployeeNameSnapshot,
                    files.Count > 1 ? match.ChargeToName : null);
                return (bytes, fileName, string.IsNullOrWhiteSpace(mime) ? "application/pdf" : mime);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not download filed expense PDF {FileId} for {ReportId}.",
                    match.DriveFileId, report.ExpenseReportId);
                return null;
            }
        }

        private async Task TryDeleteFiledPacketAsync(ExpenseReport report, string? fileId)
        {
            if (string.IsNullOrEmpty(fileId))
                return;

            var access = await TryGetAppDriveAsync();
            if (access.Token == null)
            {
                _logger.LogWarning(
                    "Could not delete filed expense PDF {FileId} for {ReportId}: App Drive is not connected.",
                    fileId, report.ExpenseReportId);
                return;
            }

            try
            {
                await _drive.DeleteFileAsync(access.Token, fileId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete filed expense PDF {FileId} for {ReportId}.",
                    fileId, report.ExpenseReportId);
            }
        }

        private async Task RelocateExpenseReceiptsAsync(ExpenseReport report)
        {
            var driveReceipts = report.Lines
                .SelectMany(l => l.Receipts ?? [])
                .Where(r => !string.IsNullOrEmpty(r.DriveFileId))
                .ToList();
            if (driveReceipts.Count == 0)
            {
                foreach (var line in report.Lines)
                {
                    foreach (var receipt in line.Receipts ?? [])
                    {
                        receipt.OriginalFileName = ExpenseDriveNamingRules.ReplaceLeadingDate(
                            receipt.OriginalFileName, line.Date);
                    }
                }
                return;
            }

            var access = await TryGetAppDriveAsync();
            if (access.Token == null || access.ExpensesFolderId == null)
                return;

            try
            {
                var periodFolderId = await EnsureExpensePeriodFolderAsync(
                    access.Token, access.ExpensesFolderId, report);
                foreach (var line in report.Lines)
                {
                    foreach (var receipt in line.Receipts ?? [])
                    {
                        if (string.IsNullOrEmpty(receipt.DriveFileId))
                            continue;
                        try
                        {
                            var ext = Path.GetExtension(receipt.OriginalFileName);
                            if (string.IsNullOrEmpty(ext))
                                ext = ".bin";
                            var desired = ExpenseDriveNamingRules.ReceiptFileName(
                                line.Date, line.Description, ext);
                            var current = await _drive.GetFileAsync(access.Token, receipt.DriveFileId);
                            var parents = current.Parents ?? [];
                            if (string.Equals(current.Name, desired, StringComparison.Ordinal)
                                && parents.Contains(periodFolderId))
                            {
                                receipt.OriginalFileName = ExpenseDriveNamingRules.ReplaceLeadingDate(
                                    receipt.OriginalFileName, line.Date);
                                continue;
                            }

                            var unique = await UniqueReceiptNameAsync(
                                access.Token, periodFolderId, desired, receipt.DriveFileId);
                            await _drive.MoveAndRenameFileAsync(
                                access.Token, receipt.DriveFileId, periodFolderId, unique);
                            receipt.OriginalFileName = ExpenseDriveNamingRules.ReplaceLeadingDate(
                                receipt.OriginalFileName, line.Date);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not relocate expense receipt {ReceiptId} for report {ReportId}.",
                                receipt.ExpenseReceiptId, report.ExpenseReportId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not relocate expense receipts for report {ReportId}.",
                    report.ExpenseReportId);
            }
        }

        private async Task<IActionResult?> ApplyChargeToAsync(ExpenseReport report, string? organizationId, bool required)
        {
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                if (required)
                    return new BadRequestObjectResult("Charge to is required.");
                report.ChargeToOrganizationId = null;
                report.ChargeToName = null;
                return null;
            }

            var org = await _dbContext.Organizations.AsNoTracking()
                .FirstOrDefaultAsync(row => row.OrganizationId == organizationId.Trim());
            if (org == null)
                return new BadRequestObjectResult("Charge to was not found.");

            report.ChargeToOrganizationId = org.OrganizationId;
            report.ChargeToName = org.Name.Length <= 100 ? org.Name : org.Name[..100];
            return null;
        }

        private List<string> DeleteLinesAndReceipts(IReadOnlyList<ExpenseLine> lines)
        {
            var driveIds = new List<string>();
            foreach (var line in lines)
            {
                foreach (var receipt in (line.Receipts ?? []).ToList())
                {
                    if (!string.IsNullOrEmpty(receipt.DriveFileId))
                        driveIds.Add(receipt.DriveFileId);
                    _dbContext.ExpenseReceipts.Remove(receipt);
                }

                _dbContext.ExpenseLines.Remove(line);
            }

            return driveIds;
        }

        private async Task TryDeleteDriveFilesAsync(IReadOnlyList<string> fileIds)
        {
            if (fileIds.Count == 0)
                return;
            var access = await TryGetAppDriveAsync();
            if (access.Token == null)
                return;
            foreach (var fileId in fileIds.Distinct(StringComparer.Ordinal))
            {
                try
                {
                    await _drive.DeleteFileAsync(access.Token, fileId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete Drive file {FileId}.", fileId);
                }
            }
        }

        private async Task<string> UploadReceiptToDriveAsync(
            string token,
            string expensesRootId,
            ExpenseReport report,
            ExpenseLine line,
            string originalFileName,
            string mimeType,
            byte[] bytes)
        {
            var periodFolderId = await EnsureExpensePeriodFolderAsync(token, expensesRootId, report);

            var ext = Path.GetExtension(originalFileName);
            if (string.IsNullOrEmpty(ext))
                ext = ".bin";
            var fileName = ExpenseDriveNamingRules.ReceiptFileName(line.Date, line.Description, ext);
            var unique = await UniqueReceiptNameAsync(token, periodFolderId, fileName, ignoreFileId: null);

            using var stream = new MemoryStream(bytes);
            var uploaded = await _drive.UploadFileAsync(token, stream, unique, mimeType, periodFolderId);
            if (string.IsNullOrEmpty(uploaded.Id))
                throw new InvalidOperationException("Drive upload returned no file id.");

            return uploaded.Id;
        }

        private async Task<string> EnsureExpensePeriodFolderAsync(
            string token, string expensesRootId, ExpenseReport report)
        {
            var user = await _dbContext.ApplicationUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == report.UserId);
            var periodFolderName = ExpenseDriveNamingRules.ReceiptPeriodFolderName(
                report.Status, report.Year, report.Month);

            var existingFolderId = report.DriveUserFolderId
                ?? await _dbContext.ExpenseReports.AsNoTracking()
                    .Where(r => r.UserId == report.UserId && r.DriveUserFolderId != null)
                    .Select(r => r.DriveUserFolderId)
                    .FirstOrDefaultAsync();

            Google.Apis.Drive.v3.Data.File userFolder;
            if (!string.IsNullOrEmpty(existingFolderId))
            {
                userFolder = new Google.Apis.Drive.v3.Data.File { Id = existingFolderId };
            }
            else
            {
                var userFolderName = ExpenseDriveNamingRules.UserFolderName(
                    user?.LastName ?? "", user?.FirstName ?? "", report.UserId);
                userFolder = await _drive.FindOrCreateFolderAsync(token, expensesRootId, userFolderName);
            }

            var periodFolder = await _drive.FindOrCreateFolderAsync(token, userFolder.Id, periodFolderName);
            report.DriveUserFolderId = userFolder.Id;
            report.DrivePeriodFolderId = periodFolder.Id;
            return periodFolder.Id;
        }

        private async Task<string> UniqueReceiptNameAsync(
            string token, string folderId, string fileName, string? ignoreFileId)
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            var unique = fileName;
            for (var n = 2; n < 50; n++)
            {
                var existing = await _drive.FindChildByNameAsync(token, folderId, unique);
                if (existing == null || existing.Id == ignoreFileId)
                    return unique;
                unique = $"{stem}_{n}{ext}";
            }

            throw new InvalidOperationException(
                "Too many receipts with the same name in that folder. Rename one and try again.");
        }
    }
}

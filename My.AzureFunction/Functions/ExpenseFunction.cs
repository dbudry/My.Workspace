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

namespace My.Functions
{
    public class ExpenseFunction
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IValidator<CreateExpenseReportDto> _createValidator;
        private readonly IValidator<UpdateExpenseReportDto> _updateValidator;
        private readonly IValidator<UpdateExpenseSettingsDto> _settingsValidator;
        private readonly GoogleDriveService _drive;
        private readonly ExpenseStatementPdfService _pdf;
        private readonly ExcelWorkbookPdfConverter _excelPdf;
        private readonly ILogger<ExpenseFunction> _logger;

        public ExpenseFunction(
            ApplicationDbContext dbContext,
            IValidator<CreateExpenseReportDto> createValidator,
            IValidator<UpdateExpenseReportDto> updateValidator,
            IValidator<UpdateExpenseSettingsDto> settingsValidator,
            GoogleDriveService drive,
            ExpenseStatementPdfService pdf,
            ExcelWorkbookPdfConverter excelPdf,
            ILogger<ExpenseFunction> logger)
        {
            _dbContext = dbContext;
            _createValidator = createValidator;
            _updateValidator = updateValidator;
            _settingsValidator = settingsValidator;
            _drive = drive;
            _pdf = pdf;
            _excelPdf = excelPdf;
            _logger = logger;
        }

        [Function("GetExpenseContext")]
        public async Task<IActionResult> GetContextAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/context")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _) is IActionResult unauth) return unauth;

            var settings = await LoadExpenseSettingsAsync();
            string? homeName = null;
            if (!string.IsNullOrEmpty(settings.HomeOrganizationId))
            {
                var org = await _dbContext.Organizations.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.OrganizationId == settings.HomeOrganizationId);
                homeName = org?.Name;
            }

            return new OkObjectResult(new ExpenseContextDto
            {
                HomeOrganizationId = settings.HomeOrganizationId,
                HomeOrganizationName = homeName,
                MileageRatePerMile = settings.MileageRate,
                Categories = ExpenseCategoryRules.CategoryChoices
                    .Select(c => new ExpenseChoiceDto { Value = c.Value, Label = c.Label })
                    .ToList(),
                TransportationCodes = ExpenseCategoryRules.TransportationCodes
                    .Select(c => new ExpenseChoiceDto { Value = c.Value, Label = c.Label })
                    .ToList(),
                MiscellaneousCodes = ExpenseCategoryRules.MiscellaneousCodes
                    .Select(c => new ExpenseChoiceDto { Value = c.Value, Label = c.Label })
                    .ToList()
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
                "Manager approval signature image for Form 87-43 PDF.");
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
                .FirstOrDefaultAsync(r => r.ExpenseReportId == id);
            if (report == null || !ExpenseReportRules.CanView(report.UserId, userId, IsExpensesManager(principal)))
                return new NotFoundObjectResult("Expense report not found.");

            var legacy = string.Equals(req.Query["layout"], "legacy", StringComparison.OrdinalIgnoreCase);
            try
            {
                if (!legacy && !string.IsNullOrEmpty(report.DriveFiledPdfFileId))
                {
                    var filed = await TryDownloadFiledPacketAsync(report);
                    if (filed == null)
                        return new ObjectResult("Couldn't load the filed expense PDF.") { StatusCode = 502 };
                    return new FileContentResult(filed.Value.Bytes, filed.Value.ContentType)
                    {
                        FileDownloadName = filed.Value.FileName
                    };
                }

                var pdf = await BuildReportPdfAsync(report, legacy);
                return new FileContentResult(pdf.Bytes, pdf.ContentType)
                {
                    FileDownloadName = pdf.FileName
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expense PDF failed for {ReportId}.", id);
                var message = legacy && ex is InvalidOperationException
                    ? ex.Message
                    : "Couldn't build the expense PDF.";
                return new ObjectResult(message) { StatusCode = 502 };
            }
        }

        [Function("GetExpenseReports")]
        public async Task<IActionResult> GetMineAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/reports")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth) return unauth;

            var rows = await ProjectList(
                    _dbContext.ExpenseReports.AsNoTracking()
                        .Where(r => r.UserId == userId)
                        .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month))
                .ToListAsync();

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
            if (years.Count > 0)
                query = query.Where(r => years.Contains(r.Year));
            if (months.Count > 0)
                query = query.Where(r => months.Contains(r.Month));
            query = FilterByStatus(query, status);

            var rows = await ProjectList(
                    query.OrderByDescending(r => r.Year)
                        .ThenByDescending(r => r.Month)
                        .ThenBy(r => r.EmployeeNameSnapshot))
                .ToListAsync();

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
            if (year.HasValue)
                query = query.Where(r => r.Year == year.Value);
            if (month.HasValue)
                query = query.Where(r => r.Month == month.Value);
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

            var blocked = ExpenseReportRules.SubmitBlockedReason(report.Status, report.Lines.Count);
            if (blocked != null)
                return new ConflictObjectResult(blocked);

            try
            {
                var packet = await BuildReportPdfAsync(report, legacy: false);
                report.DriveFiledPdfFileId = await UploadFiledPacketAsync(report, packet.Bytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not file expense PDF for {ReportId}.", id);
                var message = ex is InvalidOperationException
                    ? ex.Message
                    : "Couldn't file the expense PDF. The report was not submitted.";
                return new ObjectResult(message) { StatusCode = 502 };
            }

            report.Status = ExpenseStatusRules.Submitted;
            report.SubmittedAt = DateTime.UtcNow;
            report.SubmittedByUserId = userId;
            report.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var reloaded = await LoadOwnedReportAsync(report.ExpenseReportId, userId, tracking: false);
            return new OkObjectResult(await ToDtoAsync(reloaded!));
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
                .FirstOrDefaultAsync(r => r.ExpenseReportId == id);
            if (report == null)
                return new NotFoundObjectResult("Expense report not found.");

            var blocked = ExpenseReportRules.UnsubmitBlockedReason(report.Status);
            if (blocked != null)
                return new ConflictObjectResult(blocked);

            var filedId = report.DriveFiledPdfFileId;
            report.DriveFiledPdfFileId = null;
            report.Status = ExpenseStatusRules.Draft;
            report.SubmittedAt = null;
            report.SubmittedByUserId = null;
            report.ReimbursedAt = null;
            report.ReimbursedByUserId = null;
            report.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
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

            report.Status = ExpenseStatusRules.Reimbursed;
            report.ReimbursedAt = DateTime.UtcNow;
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

            var exists = await _dbContext.ExpenseReports
                .AnyAsync(r => r.UserId == userId && r.Year == dto!.Year && r.Month == dto.Month);
            if (exists)
                return new ConflictObjectResult("You already have a report for that month.");

            var user = await _dbContext.ApplicationUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return new NotFoundObjectResult("User not found.");

            var settings = await LoadExpenseSettingsAsync();
            var userPrefs = await _dbContext.UserSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId);
            var (coverStart, coverEnd) = ExpenseReportRules.DefaultCoverPeriod(dto!.Year, dto.Month);
            var now = DateTime.UtcNow;

            var report = new ExpenseReport
            {
                ExpenseReportId = Guid.NewGuid().ToString("N"),
                UserId = userId,
                Year = dto.Year,
                Month = dto.Month,
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

            if (dto!.Lines.Any(l => !ExpenseReportRules.IsLineDateInMonth(l.Date, report.Year, report.Month)))
                return new BadRequestObjectResult(ExpenseReportRules.LineDateMonthMessage);

            var coverStart = ExpenseLineRules.CalendarDate(dto.CoverStart);
            var coverEnd = ExpenseLineRules.CalendarDate(dto.CoverEnd);
            if (!ExpenseReportRules.IsLineDateInMonth(coverStart, report.Year, report.Month)
                || !ExpenseReportRules.IsLineDateInMonth(coverEnd, report.Year, report.Month))
                return new BadRequestObjectResult(ExpenseReportRules.CoverPeriodMonthMessage);

            report.CoverStart = coverStart;
            report.CoverEnd = coverEnd;
            report.ReportDate = coverEnd;

            var userPrefs = await _dbContext.UserSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId);
            report.AddressSnapshot = userPrefs?.ExpenseHomeAddress;
            report.Purpose = dto.Purpose;

            var existing = report.Lines.ToDictionary(l => l.ExpenseLineId, StringComparer.Ordinal);
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
                var nextDate = ExpenseLineRules.CalendarDate(lineDto.Date);
                var nextDescription = lineDto.Description.Trim();
                if (!string.IsNullOrWhiteSpace(lineDto.ExpenseLineId)
                    && existing.TryGetValue(lineDto.ExpenseLineId, out var found))
                {
                    line = found;
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
                line.Amount = ExpenseLineRules.ComputeAmount(
                    category, lineDto.Miles, lineDto.Amount, report.MileageRateSnapshot, transportCode);
                line.TransportationCode = isTransport ? transportCode : null;
                line.MiscellaneousCode = isMisc ? ExpenseCategoryRules.NormalizeCode(lineDto.MiscellaneousCode) : null;
                line.MealBreakfast = isMeals && lineDto.MealBreakfast;
                line.MealLunch = isMeals && lineDto.MealLunch;
                line.MealDinner = isMeals && lineDto.MealDinner;
                line.SortOrder = sort++;
                keep.Add(line.ExpenseLineId);
            }

            var removedDriveIds = report.Lines
                .Where(l => !keep.Contains(l.ExpenseLineId))
                .SelectMany(l => (l.Receipts ?? []).Select(r => r.DriveFileId))
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .ToList();
            foreach (var line in report.Lines.Where(l => !keep.Contains(l.ExpenseLineId)).ToList())
                report.Lines.Remove(line);

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
                    .Include(r => r.Department)
                    .AsQueryable()
                : _dbContext.ExpenseReports.AsNoTracking()
                    .Include(r => r.Lines).ThenInclude(l => l.Receipts)
                    .Include(r => r.Department);

            return await query
                .FirstOrDefaultAsync(r => r.ExpenseReportId == id && r.UserId == userId);
        }

        private async Task<ExpenseReport?> LoadReportAsync(string id, string userId, bool isManager, bool tracking)
        {
            var query = tracking
                ? _dbContext.ExpenseReports.Include(r => r.Lines).Include(r => r.Department).AsQueryable()
                : _dbContext.ExpenseReports.AsNoTracking().Include(r => r.Lines).Include(r => r.Department);

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

        private static IQueryable<ExpenseReportListDto> ProjectList(IQueryable<ExpenseReport> source) =>
            source.Select(r => new ExpenseReportListDto
            {
                ExpenseReportId = r.ExpenseReportId,
                UserId = r.UserId,
                EmployeeName = r.EmployeeNameSnapshot ?? "",
                Year = r.Year,
                Month = r.Month,
                CoverStart = r.CoverStart,
                CoverEnd = r.CoverEnd,
                ReportDate = r.ReportDate,
                Status = r.Status,
                LineCount = r.Lines.Count,
                TotalAmount = r.Lines.Sum(l => l.Amount),
                SubmittedAt = r.SubmittedAt,
                ReimbursedAt = r.ReimbursedAt,
                UpdatedAt = r.UpdatedAt
            });

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
                        MileageRateSnapshot = report.MileageRateSnapshot
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
                            Miles = line.Miles,
                            TransportationCode = transport,
                            MiscellaneousCode = line.MiscellaneousCode,
                            MealBreakfast = line.MealBreakfast,
                            MealLunch = line.MealLunch,
                            MealDinner = line.MealDinner,
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
            var settings = await LoadExpenseSettingsAsync();
            var (sig, _) = await LoadApprovalSignatureAsync();
            return new ExpenseSettingsDto
            {
                MileageRatePerMile = settings.MileageRate,
                HasApprovalSignature = sig is { Length: > 0 }
            };
        }

        private async Task<(byte[] Bytes, string FileName, string ContentType)> BuildReportPdfAsync(ExpenseReport report, bool legacy = false)
        {
            var settings = await LoadExpenseSettingsAsync();
            string? homeName = null;
            if (!string.IsNullOrEmpty(settings.HomeOrganizationId))
            {
                var org = await _dbContext.Organizations.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.OrganizationId == settings.HomeOrganizationId);
                homeName = org?.Name;
            }

            var ownerSettings = await _dbContext.UserSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == report.UserId);
            var (managerSig, _) = await LoadApprovalSignatureAsync();

            var mapped = report.Lines.OrderBy(l => l.SortOrder).Select(l =>
            {
                var (category, transport) = ExpenseCategoryRules.ForStorage(l.Category, l.TransportationCode);
                return ExpenseForm8743Rules.MapLine(
                    l.Date, l.Description, category, l.Amount, l.Miles, transport,
                    l.MiscellaneousCode, l.MealBreakfast, l.MealLunch, l.MealDinner);
            }).ToList();

            var coverStart = ExpenseLineRules.CalendarDate(report.CoverStart);
            var coverEnd = ExpenseLineRules.CalendarDate(report.CoverEnd);
            var reportDate = ExpenseLineRules.CalendarDate(report.ReportDate);

            var safeName = string.Join("_", (report.EmployeeNameSnapshot ?? "Employee")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var fileName = $"{report.Year}_{report.Month:00}_{safeName}_Expenses.pdf";

            var model = new ExpenseForm8743Model
            {
                CompanyName = string.IsNullOrWhiteSpace(homeName) ? "Home Company" : homeName,
                EmployeeName = report.EmployeeNameSnapshot ?? "",
                PlantOrLocation = string.IsNullOrWhiteSpace(report.PlantOrLocation)
                    ? ExpenseForm8743Rules.DefaultPlant
                    : report.PlantOrLocation,
                ChargeTo = homeName ?? "Home Company",
                Address = string.IsNullOrWhiteSpace(report.AddressSnapshot)
                    ? ownerSettings?.ExpenseHomeAddress
                    : report.AddressSnapshot,
                CoverStart = coverStart,
                CoverEnd = coverEnd,
                ReportDate = reportDate,
                MileageRate = report.MileageRateSnapshot,
                Purpose = report.Purpose,
                Lines = mapped,
                EmployeeSignature = ownerSettings?.ExpenseSignature,
                ManagerSignature = managerSig,
                FileName = fileName
            };

            var receiptPages = new List<ExpenseReceiptPdfPage>();
            foreach (var line in report.Lines.OrderBy(l => l.SortOrder))
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

            if (!legacy)
                return (_pdf.BuildPacket(model, receiptPages), fileName, "application/pdf");

            var templatePath = Path.Combine(AppContext.BaseDirectory, "Templates",
                ExpenseForm8743ExcelLayout.TemplateFileName);
            if (!File.Exists(templatePath))
                throw new InvalidOperationException("Legacy Form 87-43 template is missing.");
            var xlsx = ExpenseForm8743ExcelFiller.Fill(File.ReadAllBytes(templatePath), model);
            try
            {
                var formPdf = _excelPdf.ConvertToPdf(xlsx);
                return (_pdf.BuildPacket(formPdf, receiptPages), fileName, "application/pdf");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Excel could not print legacy Form 87-43 for {ReportId}; returning workbook.",
                    report.ExpenseReportId);
                var xlsxName = Path.ChangeExtension(fileName, ".xlsx");
                return (xlsx, xlsxName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            }
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
                        Miles = l.Miles,
                        TransportationCode = transport,
                        MiscellaneousCode = l.MiscellaneousCode,
                        MealBreakfast = l.MealBreakfast,
                        MealLunch = l.MealLunch,
                        MealDinner = l.MealDinner,
                        SortOrder = l.SortOrder,
                        Receipts = grouped.TryGetValue(l.ExpenseLineId, out var list) ? list : []
                    };
                })
                .ToList();

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
                EmployeeNameSnapshot = report.EmployeeNameSnapshot,
                AddressSnapshot = report.AddressSnapshot,
                MileageRateSnapshot = report.MileageRateSnapshot,
                CreatedAt = report.CreatedAt,
                UpdatedAt = report.UpdatedAt,
                Lines = lines,
                TotalAmount = lines.Sum(l => l.Amount)
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

        private async Task<string> UploadFiledPacketAsync(ExpenseReport report, byte[] bytes)
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

            var fileName = ExpenseDriveNamingRules.FiledPacketFileName(
                report.Year, report.Month, report.EmployeeNameSnapshot, report.ExpenseReportId);

            var existing = await _drive.FindChildByNameAsync(access.Token, periodFolder.Id, fileName);
            if (existing != null)
                await _drive.DeleteFileAsync(access.Token, existing.Id);

            if (!string.IsNullOrEmpty(report.DriveFiledPdfFileId)
                && report.DriveFiledPdfFileId != existing?.Id)
            {
                try
                {
                    await _drive.DeleteFileAsync(access.Token, report.DriveFiledPdfFileId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not replace previous filed expense PDF {FileId}.",
                        report.DriveFiledPdfFileId);
                }
            }

            using var stream = new MemoryStream(bytes);
            var uploaded = await _drive.UploadFileAsync(
                access.Token, stream, fileName, "application/pdf", periodFolder.Id);
            if (string.IsNullOrEmpty(uploaded.Id))
                throw new InvalidOperationException("Drive upload returned no file id for the filed expense PDF.");
            return uploaded.Id;
        }

        private async Task<(byte[] Bytes, string FileName, string ContentType)?> TryDownloadFiledPacketAsync(
            ExpenseReport report)
        {
            if (string.IsNullOrEmpty(report.DriveFiledPdfFileId))
                return null;

            var access = await TryGetAppDriveAsync();
            if (access.Token == null)
                return null;

            try
            {
                var (bytes, mime) = await _drive.DownloadFileContentAsync(
                    access.Token, report.DriveFiledPdfFileId);
                var fileName = ExpenseDriveNamingRules.FiledPacketFileName(
                    report.Year, report.Month, report.EmployeeNameSnapshot, report.ExpenseReportId);
                return (bytes, fileName, string.IsNullOrWhiteSpace(mime) ? "application/pdf" : mime);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not download filed expense PDF {FileId} for {ReportId}.",
                    report.DriveFiledPdfFileId, report.ExpenseReportId);
                return null;
            }
        }

        private async Task TryDeleteFiledPacketAsync(ExpenseReport report, string? fileId = null)
        {
            fileId ??= report.DriveFiledPdfFileId;
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
            var periodFolderName = ExpenseDriveNamingRules.PeriodFolderName(report.Year, report.Month);

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

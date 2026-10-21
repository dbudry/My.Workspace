using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using My.DAL.Models;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Shared.Constants;
using My.Shared.Dtos.Expenses;
using My.Shared.Rules;
using My.Shared.Validation;

namespace My.Functions
{
    public partial class ExpenseFunction
    {
        [Function("CreateExpenseCurrency")]
        public async Task<IActionResult> CreateCurrencyAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses/settings/currencies")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(
                req, new UpsertExpenseCurrencyDtoValidator());
            if (validationError != null)
                return validationError;

            var code = ExpenseCurrencyRules.NormalizeCode(dto!.Code);
            if (ExpenseCurrencyRules.IsUsd(code))
                return new ConflictObjectResult("US dollars are already on the list.");

            var existing = await _dbContext.ExpenseCurrencies.FirstOrDefaultAsync(row => row.Code == code);
            if (existing != null)
            {
                return new ConflictObjectResult(existing.IsRemoved
                    ? $"{code} was removed and is still on an expense line, so it cannot be added again."
                    : $"{code} already exists.");
            }

            var policy = await LoadExpensePolicyAsync();
            var sort = await _dbContext.ExpenseCurrencies.Select(row => (int?)row.SortOrder).MaxAsync() ?? 0;
            _dbContext.ExpenseCurrencies.Add(new ExpenseCurrency
            {
                ExpenseCurrencyId = Guid.NewGuid().ToString("N"),
                Code = code,
                Name = dto.Name.Trim(),
                Color = ExpenseCurrencyRules.NormalizeColor(dto.Color, ExpenseCurrencyRules.DefaultEurColor),
                RateToUsd = ExpenseCurrencyRules.NormalizeRate(
                    code,
                    policy.UserEntersRates ? 1m : dto.RateToUsd,
                    policy.CompanyCurrencyCode),
                IsActive = dto.IsActive,
                SortOrder = sort + 1
            });
            await _dbContext.SaveChangesAsync();
            return new OkObjectResult(await LoadExpenseSettingsDtoAsync());
        }

        [Function("UpdateExpenseCurrency")]
        public async Task<IActionResult> UpdateCurrencyAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "expenses/settings/currencies/{id}")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(
                req, new UpsertExpenseCurrencyDtoValidator());
            if (validationError != null)
                return validationError;

            var row = await _dbContext.ExpenseCurrencies.FirstOrDefaultAsync(currency => currency.ExpenseCurrencyId == id);
            if (row == null || row.IsRemoved)
                return new NotFoundObjectResult("Currency not found.");

            var policy = await LoadExpensePolicyAsync();
            var home = policy.CompanyCurrencyCode;
            var code = ExpenseCurrencyRules.NormalizeCode(dto!.Code);
            var isUsdRow = row.IsLocked || ExpenseCurrencyRules.IsUsd(row.Code);
            var isHomeRow = ExpenseCurrencyRules.IsHome(row.Code, home);
            if (isUsdRow && !ExpenseCurrencyRules.IsUsd(code))
                return new ConflictObjectResult("US dollars stay on the list.");
            if (isHomeRow && !ExpenseCurrencyRules.IsHome(code, home))
                return new ConflictObjectResult("The default currency stays on the list.");
            if ((isUsdRow || isHomeRow) && !dto.IsActive)
                return new ConflictObjectResult(isHomeRow
                    ? "The default currency stays active at a rate of 1."
                    : "US dollars stay on the list.");

            if (isUsdRow)
                code = ExpenseCurrencyRules.UsdCode;
            else if (isHomeRow)
                code = ExpenseCurrencyRules.NormalizeCode(home);
            else if (!string.Equals(row.Code, code, StringComparison.Ordinal))
            {
                var used = await _dbContext.ExpenseLines.AnyAsync(line => line.CurrencyCode == row.Code);
                if (used)
                    return new ConflictObjectResult($"{row.Code} is already on an expense line, so its code cannot change.");
                var taken = await _dbContext.ExpenseCurrencies.AnyAsync(currency =>
                    currency.ExpenseCurrencyId != row.ExpenseCurrencyId && currency.Code == code);
                if (taken)
                    return new ConflictObjectResult($"{code} already exists.");
            }

            decimal rate;
            if (ExpenseCurrencyRules.IsHome(code, home))
                rate = 1m;
            else if (policy.UserEntersRates)
                rate = row.RateToUsd <= 0m ? 1m : row.RateToUsd;
            else
                rate = ExpenseCurrencyRules.NormalizeRate(code, dto.RateToUsd, home);

            row.Code = code;
            row.Name = dto.Name.Trim();
            row.Color = ExpenseCurrencyRules.NormalizeColor(
                dto.Color,
                ExpenseCurrencyRules.IsUsd(code) ? ExpenseCurrencyRules.DefaultUsdColor : ExpenseCurrencyRules.DefaultEurColor);
            row.RateToUsd = rate;
            row.IsActive = isUsdRow || isHomeRow || dto.IsActive;
            await _dbContext.SaveChangesAsync();
            return new OkObjectResult(await LoadExpenseSettingsDtoAsync());
        }

        [Function("DeleteExpenseCurrency")]
        public async Task<IActionResult> DeleteCurrencyAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "expenses/settings/currencies/{id}")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var row = await _dbContext.ExpenseCurrencies.FirstOrDefaultAsync(currency => currency.ExpenseCurrencyId == id);
            if (row == null || row.IsRemoved)
                return new NotFoundObjectResult("Currency not found.");
            var policy = await LoadExpensePolicyAsync();
            if (row.IsLocked || ExpenseCurrencyRules.IsUsd(row.Code))
                return new ConflictObjectResult("US dollars stay on the list.");
            if (ExpenseCurrencyRules.IsHome(row.Code, policy.CompanyCurrencyCode))
                return new ConflictObjectResult("The default currency stays on the list.");

            var used = await _dbContext.ExpenseLines.AnyAsync(line => line.CurrencyCode == row.Code);
            if (used)
            {
                row.IsRemoved = true;
                row.IsActive = false;
            }
            else
            {
                _dbContext.ExpenseCurrencies.Remove(row);
            }

            await _dbContext.SaveChangesAsync();
            return new OkObjectResult(await LoadExpenseSettingsDtoAsync());
        }

        [Function("ReturnExpenseLine")]
        public async Task<IActionResult> ReturnLineAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses/reports/{id}/lines/{lineId}/return")] HttpRequestData req,
            string id,
            string lineId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth)
                return unauth;

            var policy = await LoadExpensePolicyAsync();
            if (!policy.CreditBucketEnabled)
                return new ConflictObjectResult("Credits are turned off.");
            if (ExpenseCreditEntryRules.IsOnForm(policy.CreditEntryMode))
                return new ConflictObjectResult("Credits are entered on the expense form.");

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(
                req, new ReturnExpenseLineDtoValidator());
            if (validationError != null)
                return validationError;

            var report = await LoadReturnReportAsync(id);
            if (report == null)
                return new NotFoundObjectResult("Expense report not found.");
            if (!CanMarkReturn(report, userId, principal))
                return new NotFoundObjectResult("Expense report not found.");
            if (!ExpenseStatusRules.IsReimbursed(report.Status))
                return new ConflictObjectResult("Mark the expense paid before crediting a line.");

            var line = report.Lines.FirstOrDefault(item => item.ExpenseLineId == lineId);
            if (line == null)
                return new NotFoundObjectResult("Expense line not found.");

            if (line.ReturnedAt != null)
            {
                var pools = await LoadCreditPoolsAsync([report.UserId]);
                if (pools.TryGetValue(report.UserId, out var pool)
                    && ExpenseCreditRules.HasBeenUsed(line.ExpenseLineId, pool.Sources, pool.Lines, policy.CompanyCurrencyCode))
                    return new ConflictObjectResult("This credit has been applied to a later expense.");
            }

            var currencies = await _dbContext.ExpenseCurrencies.AsNoTracking().ToListAsync();
            var creditCode = ExpenseCurrencyRules.NormalizeCode(dto!.CurrencyCode);
            var known = currencies.FirstOrDefault(currency =>
                string.Equals(currency.Code, creditCode, StringComparison.Ordinal));
            var onReport = string.Equals(ExpenseCurrencyRules.NormalizeCode(line.CurrencyCode), creditCode, StringComparison.Ordinal)
                || report.Lines.Any(item =>
                    string.Equals(ExpenseCurrencyRules.NormalizeCode(item.CurrencyCode), creditCode, StringComparison.Ordinal));
            if (known == null || ((known.IsRemoved || !known.IsActive) && !onReport))
                return new BadRequestObjectResult($"Unknown currency {creditCode}.");

            var rate = RateForCredit(report, line, creditCode, policy.CompanyCurrencyCode, currencies);
            var entered = ExpenseMoneyRules.EnteredAmount(line.SpentAmount, line.Amount, line.CurrencyCode);
            var amountError = ExpenseCreditEntryRules.RejectAmount(
                dto.Amount, creditCode, entered, line.CurrencyCode, rate, line.Amount);
            if (amountError != null)
                return new BadRequestObjectResult(amountError);

            var zone = await OwnerTimeZoneAsync(report.UserId);
            line.ReturnedAt = ExpensePaymentDateRules.ToStoredUtc(dto.ReturnDate, zone);
            line.CreditAmount = decimal.Round(dto.Amount, 2, MidpointRounding.AwayFromZero);
            line.CreditCurrencyCode = creditCode;
            line.CreditDescription = dto.Description.Trim();
            line.CreditExchangeRate = rate;
            report.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var reloaded = await LoadReportAsync(report.ExpenseReportId, userId, IsExpensesManager(principal), tracking: false);
            return new OkObjectResult(await ToDtoAsync(reloaded!));
        }

        [Function("UndoReturnExpenseLine")]
        public async Task<IActionResult> UndoReturnLineAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "expenses/reports/{id}/lines/{lineId}/return")] HttpRequestData req,
            string id,
            string lineId)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth)
                return unauth;

            var policy = await LoadExpensePolicyAsync();
            if (!policy.CreditBucketEnabled)
                return new ConflictObjectResult("Credits are turned off.");
            if (ExpenseCreditEntryRules.IsOnForm(policy.CreditEntryMode))
                return new ConflictObjectResult("Credits are entered on the expense form.");

            var report = await LoadReturnReportAsync(id);
            if (report == null)
                return new NotFoundObjectResult("Expense report not found.");
            if (!CanMarkReturn(report, userId, principal))
                return new NotFoundObjectResult("Expense report not found.");

            var line = report.Lines.FirstOrDefault(item => item.ExpenseLineId == lineId);
            if (line == null)
                return new NotFoundObjectResult("Expense line not found.");
            if (line.ReturnedAt == null)
                return new ConflictObjectResult("That line is not returned.");

            var pools = await LoadCreditPoolsAsync([report.UserId]);
            if (pools.TryGetValue(report.UserId, out var pool)
                && ExpenseCreditRules.HasBeenUsed(line.ExpenseLineId, pool.Sources, pool.Lines, policy.CompanyCurrencyCode))
                return new ConflictObjectResult("This credit has been applied to a later expense.");

            ClearLineCredit(line);
            report.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var reloaded = await LoadReportAsync(report.ExpenseReportId, userId, IsExpensesManager(principal), tracking: false);
            return new OkObjectResult(await ToDtoAsync(reloaded!));
        }

        private async Task<ExpenseReport?> LoadReturnReportAsync(string id) =>
            await _dbContext.ExpenseReports
                .Include(report => report.Lines)
                .Include(report => report.Department)
                .FirstOrDefaultAsync(report => report.ExpenseReportId == id);

        private static bool CanMarkReturn(ExpenseReport report, string userId, ClaimsPrincipal principal) =>
            string.Equals(report.UserId, userId, StringComparison.Ordinal) || IsExpensesManager(principal);

        private async Task<bool> CreditBucketEnabledAsync()
        {
            var row = await _dbContext.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(setting => setting.Key == Constants.SettingKeys.ExpensesCreditBucketEnabled);
            return string.Equals(row?.Value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string?> OwnerTimeZoneAsync(string userId) =>
            await _dbContext.UserSettings.AsNoTracking()
                .Where(settings => settings.UserId == userId)
                .Select(settings => settings.TimeZone)
                .FirstOrDefaultAsync();

        private async Task<Dictionary<string, string?>> OwnerTimeZonesAsync(IEnumerable<string> userIds)
        {
            var ids = userIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (ids.Count == 0)
                return new Dictionary<string, string?>(StringComparer.Ordinal);

            var rows = await _dbContext.UserSettings.AsNoTracking()
                .Where(settings => ids.Contains(settings.UserId))
                .Select(settings => new { settings.UserId, settings.TimeZone })
                .ToListAsync();
            return rows.ToDictionary(row => row.UserId, row => (string?)row.TimeZone, StringComparer.Ordinal);
        }

        private async Task<Dictionary<string, ExpenseCreditPool>> LoadCreditPoolsAsync(IReadOnlyCollection<string> userIds)
        {
            var result = userIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(id => id, _ => new ExpenseCreditPool(), StringComparer.Ordinal);
            if (result.Count == 0)
                return result;

            var ids = result.Keys.ToList();
            var zones = await _dbContext.UserSettings.AsNoTracking()
                .Where(settings => ids.Contains(settings.UserId))
                .Select(settings => new { settings.UserId, settings.TimeZone })
                .ToListAsync();
            foreach (var zone in zones)
            {
                if (result.TryGetValue(zone.UserId, out var pool))
                    pool.TimeZoneId = zone.TimeZone;
            }

            var home = (await LoadExpensePolicyAsync()).CompanyCurrencyCode;
            var rows = await _dbContext.ExpenseLines.AsNoTracking()
                .Where(line => ids.Contains(line.ExpenseReport.UserId))
                .Select(line => new
                {
                    line.ExpenseLineId,
                    line.ExpenseReportId,
                    line.ExpenseReport.UserId,
                    line.ExpenseReport.Status,
                    line.ExpenseReport.ChargeToOrganizationId,
                    line.Date,
                    line.Amount,
                    line.SpentAmount,
                    line.CurrencyCode,
                    line.ExchangeRateToUsd,
                    line.SortOrder,
                    line.ReturnedAt,
                    line.CreditAmount,
                    line.CreditCurrencyCode,
                    line.CreditExchangeRate
                })
                .ToListAsync();

            foreach (var row in rows)
            {
                if (!result.TryGetValue(row.UserId, out var pool))
                    continue;
                var returned = row.ReturnedAt != null;
                pool.Lines.Add(new ExpenseCreditLine
                {
                    LineId = row.ExpenseLineId,
                    ReportId = row.ExpenseReportId,
                    ChargeToOrganizationId = row.ChargeToOrganizationId,
                    ReportStatus = row.Status,
                    Date = ExpenseLineRules.CalendarDate(row.Date),
                    CurrencyCode = row.CurrencyCode,
                    SpentAmount = row.SpentAmount,
                    ExchangeRateToUsd = row.ExchangeRateToUsd,
                    AmountUsd = row.Amount,
                    SortOrder = row.SortOrder,
                    IsReturned = returned
                });
                if (returned && row.ReturnedAt != null && ExpenseStatusRules.IsReimbursed(row.Status))
                {
                    var source = CreditSourceMoney(
                        row.CreditAmount,
                        row.CreditCurrencyCode,
                        row.CreditExchangeRate,
                        row.CurrencyCode,
                        row.SpentAmount,
                        row.ExchangeRateToUsd,
                        row.Amount,
                        home);
                    pool.Sources.Add(new ExpenseCreditSource
                    {
                        LineId = row.ExpenseLineId,
                        ReportId = row.ExpenseReportId,
                        ChargeToOrganizationId = row.ChargeToOrganizationId,
                        ReturnDate = ExpensePaymentDateRules.ToCalendarDate(row.ReturnedAt.Value, pool.TimeZoneId),
                        CurrencyCode = source.Code,
                        SpentAmount = source.Spent,
                        ExchangeRateToUsd = source.Rate,
                        AmountUsd = source.Usd
                    });
                }
            }

            return result;
        }

        private static string? RejectUnknownCurrency(
            IEnumerable<ExpenseLineDto> lines,
            Dictionary<string, ExpenseLine> existing,
            IReadOnlyList<ExpenseCurrency> currencies)
        {
            foreach (var line in lines)
            {
                var (category, transport) = ExpenseCategoryRules.ForStorage(line.Category, line.TransportationCode);
                if (ExpenseCategoryRules.IsPersonalCar(category, transport))
                    continue;

                var code = ExpenseCurrencyRules.NormalizeCode(line.CurrencyCode);
                if (!ExpenseCurrencyRules.IsCodeShape(code))
                    return "Currency must be 3 to 8 letters.";

                var row = currencies.FirstOrDefault(currency =>
                    string.Equals(currency.Code, code, StringComparison.Ordinal));
                if (row == null)
                    return $"Unknown currency {code}.";

                ExpenseLine? saved = null;
                if (!string.IsNullOrWhiteSpace(line.ExpenseLineId))
                    existing.TryGetValue(line.ExpenseLineId, out saved);
                var already = saved != null
                    && string.Equals(ExpenseCurrencyRules.NormalizeCode(saved.CurrencyCode), code, StringComparison.Ordinal);
                if ((row.IsRemoved || !row.IsActive) && !already)
                    return $"{code} is retired.";
            }

            return null;
        }

        private static ExpenseCurrencyDto ToCurrencyDto(ExpenseCurrency row) => new()
        {
            ExpenseCurrencyId = row.ExpenseCurrencyId,
            Code = row.Code,
            Name = row.Name,
            Color = row.Color,
            RateToUsd = row.RateToUsd,
            IsActive = row.IsActive,
            IsLocked = row.IsLocked || ExpenseCurrencyRules.IsUsd(row.Code),
            SortOrder = row.SortOrder
        };

        private sealed record ExpensePolicy(
            string CompanyCurrencyCode,
            bool UserEntersRates,
            bool CreditBucketEnabled,
            string CreditEntryMode);

        private async Task<ExpensePolicy> LoadExpensePolicyAsync()
        {
            var keys = new[]
            {
                Constants.SettingKeys.ExpensesCompanyCurrencyCode,
                Constants.SettingKeys.ExpensesUserEntersRates,
                Constants.SettingKeys.ExpensesCreditBucketEnabled,
                Constants.SettingKeys.ExpensesCreditEntryMode
            };
            var rows = await _dbContext.AppSettings.AsNoTracking()
                .Where(setting => keys.Contains(setting.Key))
                .Select(setting => new { setting.Key, setting.Value })
                .ToListAsync();
            var values = rows.ToDictionary(row => row.Key, row => row.Value ?? "", StringComparer.Ordinal);
            string Value(string key) => values.TryGetValue(key, out var value) ? value : "";
            var code = ExpenseCurrencyRules.OptionalCode(Value(Constants.SettingKeys.ExpensesCompanyCurrencyCode))
                ?? ExpenseCurrencyRules.UsdCode;
            return new ExpensePolicy(
                code,
                string.Equals(Value(Constants.SettingKeys.ExpensesUserEntersRates), "true", StringComparison.OrdinalIgnoreCase),
                string.Equals(Value(Constants.SettingKeys.ExpensesCreditBucketEnabled), "true", StringComparison.OrdinalIgnoreCase),
                ExpenseCreditEntryRules.NormalizeMode(Value(Constants.SettingKeys.ExpensesCreditEntryMode)));
        }

        private static void ClearLineCredit(ExpenseLine line)
        {
            line.ReturnedAt = null;
            line.CreditAmount = null;
            line.CreditCurrencyCode = null;
            line.CreditDescription = null;
            line.CreditExchangeRate = null;
        }

        private static (string Code, decimal Spent, decimal Rate, decimal Usd) CreditSourceMoney(
            decimal? creditAmount,
            string? creditCurrency,
            decimal? creditRate,
            string lineCurrency,
            decimal lineSpent,
            decimal lineRate,
            decimal lineAmount,
            string home)
        {
            if (creditAmount is not > 0m)
                return (lineCurrency, lineSpent, lineRate, lineAmount);

            var code = ExpenseCurrencyRules.NormalizeCode(
                string.IsNullOrWhiteSpace(creditCurrency) ? lineCurrency : creditCurrency);
            var rate = creditRate is > 0m ? creditRate.Value : lineRate;
            if (ExpenseCurrencyRules.IsHome(code, home))
                rate = 1m;
            var spent = creditAmount.Value;
            return (code, spent, rate, ExpenseCurrencyRules.ToUsd(spent, rate));
        }

        private static decimal RateForCredit(
            ExpenseReport report,
            ExpenseLine line,
            string creditCode,
            string home,
            IReadOnlyList<ExpenseCurrency> currencies)
        {
            if (ExpenseCurrencyRules.IsHome(creditCode, home))
                return 1m;
            if (string.Equals(ExpenseCurrencyRules.NormalizeCode(line.CurrencyCode), creditCode, StringComparison.Ordinal)
                && line.ExchangeRateToUsd > 0m)
                return ExpenseCurrencyRules.NormalizeRate(creditCode, line.ExchangeRateToUsd, home);

            var sibling = report.Lines.FirstOrDefault(item =>
                string.Equals(ExpenseCurrencyRules.NormalizeCode(item.CurrencyCode), creditCode, StringComparison.Ordinal)
                && item.ExchangeRateToUsd > 0m);
            if (sibling != null)
                return ExpenseCurrencyRules.NormalizeRate(creditCode, sibling.ExchangeRateToUsd, home);

            var catalog = currencies.FirstOrDefault(currency =>
                string.Equals(currency.Code, creditCode, StringComparison.Ordinal));
            return ExpenseCurrencyRules.NormalizeRate(creditCode, catalog?.RateToUsd ?? 1m, home);
        }

        private string? SyncFormCredits(
            ExpenseReport report,
            IReadOnlyList<ExpenseFormCreditDto>? credits,
            IReadOnlyList<ExpenseCurrency> currencies,
            ExpensePolicy policy)
        {
            var incoming = credits ?? [];
            var existing = report.FormCredits.ToDictionary(credit => credit.ExpenseCreditId, StringComparer.Ordinal);
            var keep = new HashSet<string>(StringComparer.Ordinal);
            var sort = 0;
            foreach (var credit in incoming)
            {
                var code = ExpenseCurrencyRules.NormalizeCode(credit.CurrencyCode);
                var known = currencies.FirstOrDefault(currency =>
                    string.Equals(currency.Code, code, StringComparison.Ordinal));
                var already = false;
                if (!string.IsNullOrWhiteSpace(credit.ExpenseCreditId)
                    && existing.TryGetValue(credit.ExpenseCreditId, out var matched))
                {
                    already = string.Equals(
                        ExpenseCurrencyRules.NormalizeCode(matched.CurrencyCode),
                        code,
                        StringComparison.Ordinal);
                }
                if (known == null || ((known.IsRemoved || !known.IsActive) && !already))
                    return known == null ? $"Unknown currency {code}." : $"{code} is retired.";

                decimal rate;
                if (ExpenseCurrencyRules.IsHome(code, policy.CompanyCurrencyCode))
                    rate = 1m;
                else if (policy.UserEntersRates)
                {
                    if (credit.ExchangeRateToUsd <= 0m)
                        return $"Enter the exchange rate for {code}.";
                    rate = ExpenseCurrencyRules.NormalizeRate(code, credit.ExchangeRateToUsd, policy.CompanyCurrencyCode);
                }
                else
                    rate = ExpenseCurrencyRules.NormalizeRate(code, known.RateToUsd, policy.CompanyCurrencyCode);

                var id = Guid.TryParse(credit.ExpenseCreditId, out var parsed)
                    ? parsed.ToString("N")
                    : Guid.NewGuid().ToString("N");
                if (!existing.TryGetValue(id, out var row))
                {
                    row = new ExpenseCredit
                    {
                        ExpenseCreditId = id,
                        ExpenseReportId = report.ExpenseReportId
                    };
                    report.FormCredits.Add(row);
                    existing[id] = row;
                }

                row.ChargeToOrganizationId = report.ChargeToOrganizationId;
                row.CreditDate = ExpenseLineRules.CalendarDate(credit.Date);
                row.Description = credit.Description.Trim();
                row.Amount = decimal.Round(credit.Amount, 2, MidpointRounding.AwayFromZero);
                row.CurrencyCode = code;
                row.ExchangeRateToUsd = rate;
                row.SortOrder = sort++;
                keep.Add(id);
            }

            foreach (var row in report.FormCredits.Where(credit => !keep.Contains(credit.ExpenseCreditId)).ToList())
                _dbContext.ExpenseCredits.Remove(row);
            return null;
        }

        private sealed class ExpenseCreditPool
        {
            public string? TimeZoneId { get; set; }
            public List<ExpenseCreditSource> Sources { get; } = [];
            public List<ExpenseCreditLine> Lines { get; } = [];
        }
    }
}

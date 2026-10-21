using System.Text.Json;
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
        private static readonly JsonSerializerOptions NotifyJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        [Function("CreateExpenseCode")]
        public async Task<IActionResult> CreateCodeAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses/settings/codes")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(
                req, new UpsertExpenseCodeDtoValidator());
            if (validationError != null)
                return validationError;

            var code = ExpenseCategoryRules.NormalizeCode(dto!.Code)!;
            var existing = await _dbContext.ExpenseCodes.FirstOrDefaultAsync(row =>
                row.Kind == ExpenseCodeKindRules.Miscellaneous && row.Code == code);
            if (existing != null)
            {
                return new ConflictObjectResult(existing.IsRemoved
                    ? $"{code} was removed and is still on an expense line, so it cannot be added again."
                    : $"Miscellaneous code {code} already exists.");
            }

            var sort = await _dbContext.ExpenseCodes
                .Where(row => row.Kind == ExpenseCodeKindRules.Miscellaneous)
                .Select(row => (int?)row.SortOrder)
                .MaxAsync() ?? 0;

            _dbContext.ExpenseCodes.Add(new ExpenseCode
            {
                ExpenseCodeId = Guid.NewGuid().ToString("N"),
                Kind = ExpenseCodeKindRules.Miscellaneous,
                Code = code,
                Label = dto.Label.Trim(),
                IsActive = true,
                IsLocked = false,
                SortOrder = sort + 1
            });
            await _dbContext.SaveChangesAsync();
            return new OkObjectResult(await LoadExpenseSettingsDtoAsync());
        }

        [Function("UpdateExpenseCode")]
        public async Task<IActionResult> UpdateCodeAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "expenses/settings/codes/{id}")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(
                req, new UpsertExpenseCodeDtoValidator());
            if (validationError != null)
                return validationError;

            var row = await _dbContext.ExpenseCodes.FirstOrDefaultAsync(code => code.ExpenseCodeId == id);
            if (row == null)
                return new NotFoundObjectResult("Expense code not found.");
            if (row.IsLocked || row.Kind != ExpenseCodeKindRules.Miscellaneous)
                return new ConflictObjectResult("Only miscellaneous codes can be edited.");

            var code = ExpenseCategoryRules.NormalizeCode(dto!.Code)!;
            var softwareBlock = ExpenseCodeRemovalRules.RejectSoftwareEdit(row.Code, code, dto.IsActive);
            if (softwareBlock != null)
                return new ConflictObjectResult(softwareBlock);
            if (!string.Equals(code, row.Code, StringComparison.Ordinal))
            {
                var used = await _dbContext.ExpenseLines.AnyAsync(line => line.MiscellaneousCode == row.Code);
                if (used)
                    return new ConflictObjectResult($"{row.Code} is on an expense line, so the code cannot change.");
                var taken = await _dbContext.ExpenseCodes.AnyAsync(other =>
                    other.ExpenseCodeId != row.ExpenseCodeId
                    && other.Kind == ExpenseCodeKindRules.Miscellaneous
                    && other.Code == code);
                if (taken)
                    return new ConflictObjectResult($"Miscellaneous code {code} already exists.");
                row.Code = code;
            }

            row.Label = dto.Label.Trim();
            row.IsActive = dto.IsActive;
            await _dbContext.SaveChangesAsync();
            return new OkObjectResult(await LoadExpenseSettingsDtoAsync());
        }

        [Function("DeleteExpenseCode")]
        public async Task<IActionResult> DeleteCodeAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "expenses/settings/codes/{id}")] HttpRequestData req,
            string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var root = await _dbContext.ExpenseCodes.FirstOrDefaultAsync(code => code.ExpenseCodeId == id);
            if (root == null)
                return new NotFoundObjectResult("Expense code not found.");
            if (root.IsLocked || root.Kind != ExpenseCodeKindRules.Miscellaneous)
                return new ConflictObjectResult("Only miscellaneous codes can be removed.");
            if (ExpenseCodeRemovalRules.IsSoftwareCode(root.Code))
                return new ConflictObjectResult(ExpenseCodeRemovalRules.SoftwareKeptMessage);

            var catalog = await _dbContext.ExpenseCodes
                .Where(code => code.Kind == ExpenseCodeKindRules.Miscellaneous)
                .ToListAsync();
            var names = ExpenseCodeRemovalRules.CodesToRemove(root.Code, catalog.Select(code => code.Code));
            var rows = catalog
                .Where(code => names.Contains(code.Code, StringComparer.Ordinal) && !code.IsRemoved)
                .OrderByDescending(code => code.Code.Length)
                .ToList();

            foreach (var row in rows)
            {
                var used = await _dbContext.ExpenseLines.AnyAsync(line => line.MiscellaneousCode == row.Code);
                if (used)
                {
                    row.IsRemoved = true;
                    row.IsActive = false;
                }
                else
                {
                    _dbContext.ExpenseCodes.Remove(row);
                }
            }

            await _dbContext.SaveChangesAsync();
            return new OkObjectResult(await LoadExpenseSettingsDtoAsync());
        }

        [Function("UpdateExpenseNotifyUsers")]
        public async Task<IActionResult> UpdateNotifyUsersAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "expenses/settings/notify-users")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(
                req, new UpdateExpenseNotifyUsersDtoValidator());
            if (validationError != null)
                return validationError;

            var requested = (dto!.UserIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var known = await _dbContext.ApplicationUsers.AsNoTracking()
                .Where(user => requested.Contains(user.Id) && user.IsActive && !user.IsArchived)
                .Select(user => user.Id)
                .ToListAsync();
            var keep = requested.Where(id => known.Contains(id)).ToList();

            await UpsertSettingAsync(
                Constants.SettingKeys.ExpensesSubmitNotifyUserIds,
                JsonSerializer.Serialize(keep, NotifyJson),
                "User ids emailed when an expense report is submitted.");
            await _dbContext.SaveChangesAsync();
            return new OkObjectResult(await LoadExpenseSettingsDtoAsync());
        }

        [Function("GetExpenseDirectory")]
        public async Task<IActionResult> GetDirectoryAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/settings/directory")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out _, Constants.Roles.Manager) is IActionResult unauth)
                return unauth;

            var users = await _dbContext.ApplicationUsers.AsNoTracking()
                .Where(user => user.IsActive && !user.IsArchived && user.Email != null && user.Email != "")
                .OrderBy(user => user.LastName).ThenBy(user => user.FirstName)
                .Select(user => new ExpenseNotifyUserDto
                {
                    UserId = user.Id,
                    Name = (user.FirstName + " " + user.LastName).Trim(),
                    Email = user.Email!
                })
                .ToListAsync();
            return new OkObjectResult(users);
        }

        private async Task<string?> RejectUnknownCodesAsync(IReadOnlyList<ExpenseLineDto> lines)
        {
            if (lines.Count == 0)
                return null;

            var catalog = await _dbContext.ExpenseCodes.AsNoTracking().ToListAsync();
            foreach (var line in lines)
            {
                var (category, transport) = ExpenseCategoryRules.ForStorage(line.Category, line.TransportationCode);
                if (string.Equals(category, ExpenseCategoryRules.Transportation, StringComparison.Ordinal))
                {
                    var code = ExpenseCategoryRules.NormalizeCode(transport);
                    if (string.IsNullOrEmpty(code))
                        return "A transportation line needs a code.";
                    if (!catalog.Any(row =>
                            row.Kind == ExpenseCodeKindRules.Transportation
                            && string.Equals(row.Code, code, StringComparison.OrdinalIgnoreCase)))
                        return $"Unknown transportation code {code}.";
                }

                if (string.Equals(category, ExpenseCategoryRules.Miscellaneous, StringComparison.Ordinal))
                {
                    var code = ExpenseCategoryRules.NormalizeCode(line.MiscellaneousCode);
                    if (string.IsNullOrEmpty(code))
                        return "A miscellaneous line needs a code.";
                    if (!catalog.Any(row =>
                            row.Kind == ExpenseCodeKindRules.Miscellaneous
                            && string.Equals(row.Code, code, StringComparison.OrdinalIgnoreCase)))
                        return $"Unknown miscellaneous code {code}.";
                }
            }

            return null;
        }

        private async Task<List<ExpenseCode>> LoadCodesAsync() =>
            await _dbContext.ExpenseCodes.AsNoTracking()
                .OrderBy(row => row.SortOrder).ThenBy(row => row.Code)
                .ToListAsync();

        private static List<ExpenseChoiceDto> ToChoices(IEnumerable<ExpenseCode> rows) =>
            rows.Where(row => row.IsActive && !row.IsRemoved)
                .Select(row => new ExpenseChoiceDto
                {
                    Value = row.Code,
                    Label = $"{row.Code} — {row.Label}"
                })
                .ToList();

        private static List<ExpenseChoiceDto> FallbackChoices(IReadOnlyList<(string Value, string Label)> seed) =>
            seed.Select(row => new ExpenseChoiceDto { Value = row.Value, Label = row.Label }).ToList();

        private async Task<List<string>> LoadNotifyUserIdsAsync()
        {
            var row = await _dbContext.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(setting => setting.Key == Constants.SettingKeys.ExpensesSubmitNotifyUserIds);
            if (string.IsNullOrWhiteSpace(row?.Value))
                return [];
            try
            {
                return JsonSerializer.Deserialize<List<string>>(row.Value, NotifyJson) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }
}

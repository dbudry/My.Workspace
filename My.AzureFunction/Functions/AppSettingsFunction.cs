using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.DAL.Data;
using My.Functions.Helpers;
using My.Shared.Constants;
using My.Shared.Dtos;
using My.Shared.Rules;

namespace My.Functions
{
    public class AppSettingsFunction
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AppSettingsFunction> _logger;
        private readonly IValidator<List<AppSettingDto>> _updateValidator;

        public AppSettingsFunction(
            ApplicationDbContext dbContext,
            IMemoryCache cache,
            ILogger<AppSettingsFunction> logger,
            IValidator<List<AppSettingDto>> updateValidator)
        {
            _dbContext = dbContext;
            _cache = cache;
            _logger = logger;
            _updateValidator = updateValidator;
        }

        [Function("GetAppSettings")]
        public async Task<IActionResult> GetAppSettingsAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "appsettings")] HttpRequestData req)
        {
            // Any admin (global or scoped) can read app settings — the User Manager
            // needs flags like AllowUserDelete and DataRetentionDays to render its UI.
            // Mutations stay global-Admin only (see UpdateAppSettingsAsync below).
            var principal = new ClaimsPrincipal(req.Identities);
            if (!Constants.Roles.IsAnyAdmin(principal))
                return new StatusCodeResult(403);

            var settings = await _dbContext.AppSettings
                .Select(s => new AppSettingDto
                {
                    Key = s.Key,
                    Value = s.Value,
                    Description = s.Description
                })
                .ToListAsync();

            return new OkObjectResult(settings);
        }

        [Function("GetContactTypeUsage")]
        public async Task<IActionResult> GetContactTypeUsageAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "appsettings/contact-types/usage")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (!principal.IsInRole(Constants.Roles.Admin))
                return new StatusCodeResult(403);

            var usage = await GetContactTypeUsageCountsAsync();
            return new OkObjectResult(usage);
        }

        [Function("UpdateAppSettings")]
        public async Task<IActionResult> UpdateAppSettingsAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "appsettings")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (!principal.IsInRole(Constants.Roles.Admin))
                return new StatusCodeResult(403);

            List<AppSettingDto>? dtos;
            try
            {
                dtos = await req.ReadFromJsonAsync<List<AppSettingDto>>();
            }
            catch
            {
                return new BadRequestObjectResult(RequestValidator.InvalidBodyMessage);
            }

            if (await RequestValidator.BadRequestIfInvalidAsync(_updateValidator, dtos) is { } validationError)
                return validationError;

            foreach (var dto in dtos!)
            {
                if (string.Equals(dto.Key, Constants.SettingKeys.ContactTypes, StringComparison.Ordinal))
                {
                    var existing = await _dbContext.AppSettings.FindAsync(dto.Key);
                    var oldTypes = ContactTypeRules.Parse(existing?.Value);
                    var newTypes = ContactTypeRules.Parse(dto.Value);
                    var usage = await GetContactTypeUsageCountsAsync();
                    var contactTypeError = ContactTypeRules.ValidateSettingsUpdate(oldTypes, newTypes, usage);
                    if (contactTypeError != null)
                        return new BadRequestObjectResult(contactTypeError);
                }

                var setting = await _dbContext.AppSettings.FindAsync(dto.Key);
                if (setting != null)
                {
                    setting.Value = dto.Value ?? string.Empty;
                }
                else
                {
                    // Insert new key if it doesn't exist yet (e.g. IntranetDriveParentFolderId)
                    _dbContext.AppSettings.Add(new My.DAL.Models.AppSetting
                    {
                        Key = dto.Key,
                        Value = dto.Value ?? string.Empty,
                        Description = dto.Description
                    });
                }
            }

            await _dbContext.SaveChangesAsync();

            if (dtos.Any(d => string.Equals(d.Key, Constants.SettingKeys.RateLimitEnabled, StringComparison.Ordinal)))
                RateLimitSettingsLoader.Invalidate(_cache);

            _logger.LogInformation("Admin updated app settings: {Keys}", string.Join(", ", dtos.Select(d => d.Key)));

            return new OkObjectResult(await _dbContext.AppSettings
                .Select(s => new AppSettingDto
                {
                    Key = s.Key,
                    Value = s.Value,
                    Description = s.Description
                })
                .ToListAsync());
        }

        private async Task<Dictionary<string, int>> GetContactTypeUsageCountsAsync()
        {
            var rows = await _dbContext.Contacts
                .Where(c => c.ContactType != null && c.ContactType != "")
                .GroupBy(c => c.ContactType!)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToListAsync();

            var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
                usage[row.Type] = row.Count;

            return usage;
        }
    }
}

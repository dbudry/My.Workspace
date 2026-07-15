using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using My.DAL.Data;
using My.Functions.Helpers;
using My.Shared.Constants;
using My.Shared.Dtos.Setup;
using My.Shared.Rules;

namespace My.Functions;

/// <summary>
/// First-run setup API for self-hosters. Open only until the first user exists
/// (or Setup:Completed is set). Never returns secrets.
/// </summary>
public class SetupFunction
{
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SetupFunction> _logger;

    public SetupFunction(
        ApplicationDbContext db,
        IMemoryCache cache,
        ILogger<SetupFunction> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    [Function("SetupStatus")]
    public async Task<IActionResult> GetStatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = Constants.API.Setup.Status)] HttpRequestData req)
    {
        try
        {
            if (_cache.TryGetValue(SetupState.StatusCacheKeyName, out SetupStatusDto? cached) && cached != null)
                return new OkObjectResult(cached);

            var usersExist = await _db.ApplicationUsers.AsNoTracking().AnyAsync();
            var domains = await AuthDomainSettingsLoader.LoadDatabaseValueAsync(_db, _cache);
            var resolved = GoogleIdentityRules.ResolveConfiguredDomains(domains);
            var displayName = await SetupState.GetSettingAsync(_db, _cache, Constants.SettingKeys.AppDisplayName)
                ?? "My Workspace";
            var completedFlag = await SetupState.GetSettingAsync(_db, _cache, Constants.SettingKeys.SetupCompleted);
            var setupCompleted = usersExist
                || string.Equals(completedFlag, "true", StringComparison.OrdinalIgnoreCase);

            // Lightweight DB probe
            var databaseReady = true;
            try
            {
                await _db.Database.CanConnectAsync();
            }
            catch
            {
                databaseReady = false;
            }

            var clientId = Environment.GetEnvironmentVariable("Google__ClientId")
                ?? Environment.GetEnvironmentVariable("Google:ClientId")
                ?? "";
            var clientSecret = Environment.GetEnvironmentVariable("Google__ClientSecret") ?? "";

            var dto = new SetupStatusDto
            {
                SetupCompleted = setupCompleted,
                DatabaseReady = databaseReady,
                GoogleClientIdConfiguredOnServer = !string.IsNullOrWhiteSpace(clientId)
                    && !clientId.Contains("YOUR_", StringComparison.OrdinalIgnoreCase)
                    && !clientId.Contains("placeholder", StringComparison.OrdinalIgnoreCase),
                GoogleClientSecretConfiguredOnServer = !string.IsNullOrWhiteSpace(clientSecret)
                    && !clientSecret.Contains("YOUR_", StringComparison.OrdinalIgnoreCase),
                AllowedDomainsConfigured = GoogleIdentityRules.IsDomainPolicyConfigured(resolved),
                UsersExist = usersExist,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "My Workspace" : displayName,
                AllowedEmailDomains = string.IsNullOrWhiteSpace(resolved) ? null : resolved
            };

            _cache.Set(SetupState.StatusCacheKeyName, dto, SetupState.StatusCacheTtl);
            return new OkObjectResult(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setup status failed.");
            return new OkObjectResult(new SetupStatusDto
            {
                SetupCompleted = false,
                DatabaseReady = false,
                DisplayName = "My Workspace"
            });
        }
    }

    [Function("SetupConfigure")]
    public async Task<IActionResult> ConfigureAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = Constants.API.Setup.Configure)] HttpRequestData req)
    {
        var usersExist = await _db.ApplicationUsers.AsNoTracking().AnyAsync();
        if (usersExist)
        {
            _logger.LogWarning("Setup configure rejected: users already exist.");
            return new StatusCodeResult(410);
        }

        var completedFlag = await SetupState.GetSettingAsync(_db, _cache, Constants.SettingKeys.SetupCompleted);
        if (string.Equals(completedFlag, "true", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Setup configure rejected: setup already completed.");
            return new StatusCodeResult(410);
        }

        SetupConfigureRequest? body;
        try
        {
            body = await req.ReadFromJsonAsync<SetupConfigureRequest>();
        }
        catch
        {
            return new BadRequestObjectResult(new { error = "Invalid JSON body." });
        }

        if (body is null)
            return new BadRequestObjectResult(new { error = "Body is required." });

        var domains = (body.AllowedEmailDomains ?? "").Trim();
        if (!GoogleIdentityRules.IsDomainPolicyConfigured(domains))
        {
            return new BadRequestObjectResult(new
            {
                error = "AllowedEmailDomains is required. Use a domain like example.com, a comma-separated list, or * for any verified Google account."
            });
        }

        var displayName = string.IsNullOrWhiteSpace(body.DisplayName)
            ? "My Workspace"
            : body.DisplayName.Trim();
        if (displayName.Length > 80)
            displayName = displayName[..80];

        await SetupState.UpsertSettingAsync(
            _db,
            Constants.SettingKeys.AuthAllowedEmailDomains,
            domains,
            "Comma-separated Google email domains allowed to sign in, or * for any verified account.");

        await SetupState.UpsertSettingAsync(
            _db,
            Constants.SettingKeys.AppDisplayName,
            displayName,
            "Workspace display name shown in the product UI.");

        // Keep GoogleIdentityRules key in sync if constants ever diverge (same value today).
        AuthDomainSettingsLoader.Invalidate(_cache);
        SetupState.Invalidate(_cache);

        _logger.LogInformation("Setup configure saved: displayName={DisplayName}, domains={Domains}", displayName, domains);

        return new OkObjectResult(new
        {
            ok = true,
            displayName,
            allowedEmailDomains = domains
        });
    }
}

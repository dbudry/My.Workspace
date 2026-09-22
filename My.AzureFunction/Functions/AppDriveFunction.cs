using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.DAL.Data;
using My.DAL.Models;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Functions.Services;
using My.Shared;
using My.Shared.Constants;
using My.Shared.Dtos.Expenses;
using My.Shared.Dtos.GoogleCalendar;
using My.Shared.Rules;
using My.Shared.Validation;

namespace My.Functions;

public class AppDriveFunction
{
    private readonly ApplicationDbContext db;
    private readonly GoogleDriveService drive;
    private readonly GoogleTokenEncryptor encryptor;
    private readonly RedirectUriQueryValidator redirectUriValidator;
    private readonly IValidator<GoogleCalendarCallbackDto> callbackValidator;
    private readonly IMemoryCache cache;
    private readonly ILogger<AppDriveFunction> logger;

    public AppDriveFunction(
        ApplicationDbContext db,
        GoogleDriveService drive,
        GoogleTokenEncryptor encryptor,
        RedirectUriQueryValidator redirectUriValidator,
        IValidator<GoogleCalendarCallbackDto> callbackValidator,
        IMemoryCache cache,
        ILogger<AppDriveFunction> logger)
    {
        this.db = db;
        this.drive = drive;
        this.encryptor = encryptor;
        this.redirectUriValidator = redirectUriValidator;
        this.callbackValidator = callbackValidator;
        this.cache = cache;
        this.logger = logger;
    }

    [Function("GetAppDriveStatus")]
    public async Task<IActionResult> GetStatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "appdrive/status")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireGlobalAdmin(principal, out _) is { } gate)
            return gate;

        return new OkObjectResult(await BuildStatusAsync());
    }

    [Function("GetAppDriveAuthUrl")]
    public async Task<IActionResult> GetAuthUrlAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "appdrive/authurl")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireGlobalAdmin(principal, out var userId) is { } gate)
            return gate;

        if (!drive.IsConfigured)
            return new ObjectResult("Google Drive integration is not configured on the server.") { StatusCode = 503 };

        var redirectUri = req.Query["redirectUri"];
        if (RequestValidator.BadRequestIfInvalid(redirectUriValidator, redirectUri) is { } redirectError)
            return redirectError;

        var domains = await AuthDomainSettingsLoader.ResolveAsync(db, cache);
        var hostedDomain = GoogleIdentityRules.GetSingleHostedDomainHint(domains);
        var url = drive.BuildAppDriveAuthorizationUrl(redirectUri!, state: userId, hostedDomain);
        return new OkObjectResult(new { url });
    }

    [Function("CompleteAppDriveCallback")]
    public async Task<IActionResult> CompleteCallbackAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "appdrive/callback")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireGlobalAdmin(principal, out var userId) is { } gate)
            return gate;

        var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, callbackValidator);
        if (validationError != null)
            return validationError;

        try
        {
            var (refresh, email) = await drive.ExchangeAppDriveCodeAsync(body!.Code!, body.RedirectUri!);
            var row = await GetOrCreateCredentialAsync();
            if (!string.IsNullOrEmpty(refresh))
            {
                var encrypted = encryptor.Encrypt(refresh);
                row.EncryptedRefreshToken = encrypted;
                await MirrorTokenOntoConnectingAdminAsync(userId, encrypted, email);
            }
            else if (string.IsNullOrEmpty(row.EncryptedRefreshToken))
                return new BadRequestObjectResult(ApiErrorMessages.GoogleDriveConnectFailed);

            if (!string.IsNullOrEmpty(email))
                row.Email = email;
            row.ConnectedAt = DateTime.UtcNow;
            row.ConnectedByUserId = userId;
            await db.SaveChangesAsync();

            try
            {
                await EnsureLayoutCoreAsync(row);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "App Drive connected but layout failed for {UserId}.", userId);
            }

            return new OkObjectResult(await BuildStatusAsync());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "App Drive callback failed for {UserId}.", userId);
            return new BadRequestObjectResult(ApiErrorMessages.GoogleDriveConnectFailed);
        }
    }

    [Function("EnsureAppDriveLayout")]
    public async Task<IActionResult> EnsureLayoutAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "appdrive/ensurelayout")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireGlobalAdmin(principal, out _) is { } gate)
            return gate;

        var row = await db.AppDriveCredentials.FirstOrDefaultAsync(c =>
            c.AppDriveCredentialId == AppDriveLayoutRules.CredentialId);
        if (row == null || string.IsNullOrEmpty(row.EncryptedRefreshToken))
            return new ObjectResult(AppDriveLayoutRules.NotConnectedMessage) { StatusCode = 409 };

        try
        {
            await EnsureLayoutCoreAsync(row);
            return new OkObjectResult(await BuildStatusAsync());
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Ensure App Drive layout rejected.");
            return new ObjectResult(ex.Message) { StatusCode = 409 };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ensure App Drive layout failed.");
            return new ObjectResult(ApiErrorMessages.DriveOperationFailed) { StatusCode = 502 };
        }
    }

    [Function("DisconnectAppDrive")]
    public async Task<IActionResult> DisconnectAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "appdrive/disconnect")] HttpRequestData req)
    {
        var principal = new ClaimsPrincipal(req.Identities);
        if (AuthGates.RequireGlobalAdmin(principal, out _) is { } gate)
            return gate;

        var row = await db.AppDriveCredentials.FirstOrDefaultAsync(c =>
            c.AppDriveCredentialId == AppDriveLayoutRules.CredentialId);
        if (row != null)
        {
            row.EncryptedRefreshToken = null;
            row.Email = null;
            row.SharedDriveId = null;
            row.ConnectedAt = null;
            row.ConnectedByUserId = null;
            await db.SaveChangesAsync();
        }

        return new OkObjectResult(await BuildStatusAsync());
    }

    private async Task EnsureLayoutCoreAsync(AppDriveCredential row)
    {
        var token = row.EncryptedRefreshToken!;
        var shared = await drive.ResolveSharedDriveAsync(
            token, AppDriveLayoutRules.SharedDriveId, AppDriveLayoutRules.SharedDriveName);
        row.SharedDriveId = shared.Id;

        try
        {
            await drive.ApplySharedDriveRestrictionsAsync(token, shared.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply Shared Drive restrictions for {DriveId}.", shared.Id);
        }

        var intranet = await drive.FindOrCreateFolderAsync(
            token, shared.Id, AppDriveLayoutRules.IntranetFolderName);
        await drive.FindOrCreateFolderAsync(token, intranet.Id, IntranetDriveFolderLayout.ImagesFolderName);
        await drive.FindOrCreateFolderAsync(token, intranet.Id, IntranetDriveFolderLayout.VideosFolderName);
        await drive.FindOrCreateFolderAsync(token, intranet.Id, IntranetDriveFolderLayout.DocumentsFolderName);

        var expenses = await drive.FindOrCreateFolderAsync(
            token, shared.Id, AppDriveLayoutRules.ExpensesFolderName);
        await drive.FindOrCreateFolderAsync(
            token, expenses.Id, ExpenseDriveNamingRules.FiledFolderName);

        await UpsertSettingAsync(
            Constants.SettingKeys.IntranetDriveParentFolderId,
            intranet.Id ?? "",
            "Google Drive folder ID for the Intranet root under the App Shared Drive.");
        await UpsertSettingAsync(
            Constants.SettingKeys.ExpensesDriveParentFolderId,
            expenses.Id ?? "",
            "Google Drive folder ID for the Expenses root under the App Shared Drive.");
        await db.SaveChangesAsync();
    }

    private async Task<AppDriveStatusDto> BuildStatusAsync()
    {
        var row = await db.AppDriveCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.AppDriveCredentialId == AppDriveLayoutRules.CredentialId);
        var intranet = await db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == Constants.SettingKeys.IntranetDriveParentFolderId);
        var expenses = await db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == Constants.SettingKeys.ExpensesDriveParentFolderId);

        var dto = new AppDriveStatusDto
        {
            Connected = !string.IsNullOrEmpty(row?.EncryptedRefreshToken),
            Email = row?.Email,
            SharedDriveId = row?.SharedDriveId ?? AppDriveLayoutRules.SharedDriveId,
            SharedDriveName = AppDriveLayoutRules.SharedDriveName,
            IntranetFolderId = string.IsNullOrWhiteSpace(intranet?.Value) ? null : intranet.Value.Trim(),
            ExpensesFolderId = string.IsNullOrWhiteSpace(expenses?.Value) ? null : expenses.Value.Trim()
        };

        if (!string.IsNullOrEmpty(row?.EncryptedRefreshToken))
        {
            try
            {
                var live = await drive.GetSharedDriveInfoAsync(
                    row.EncryptedRefreshToken,
                    row.SharedDriveId ?? AppDriveLayoutRules.SharedDriveId);
                if (live != null)
                {
                    dto.SharedDriveId = live.Id ?? dto.SharedDriveId;
                    if (!string.IsNullOrWhiteSpace(live.Name))
                        dto.SharedDriveName = live.Name;
                    dto.DomainUsersOnly = live.DomainUsersOnly;
                    dto.DriveMembersOnly = live.DriveMembersOnly;
                    dto.SharingFoldersRequiresOrganizerPermission =
                        live.SharingFoldersRequiresOrganizerPermission;
                    dto.CopyRequiresWriterPermission = live.CopyRequiresWriterPermission;
                    dto.RestrictedForWriters = live.RestrictedForWriters;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read Shared Drive restrictions.");
            }
        }

        return dto;
    }

    private async Task<AppDriveCredential> GetOrCreateCredentialAsync()
    {
        var row = await db.AppDriveCredentials
            .FirstOrDefaultAsync(c => c.AppDriveCredentialId == AppDriveLayoutRules.CredentialId);
        if (row != null)
            return row;

        row = new AppDriveCredential { AppDriveCredentialId = AppDriveLayoutRules.CredentialId };
        db.AppDriveCredentials.Add(row);
        return row;
    }

    private async Task MirrorTokenOntoConnectingAdminAsync(string userId, string encrypted, string? email)
    {
        var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
        if (settings == null || string.IsNullOrEmpty(settings.GoogleRefreshToken))
            return;

        // Google issues one refresh token per OAuth client. This Connect uses
        // include_granted_scopes so Calendar stays on the new token; copy it onto
        // the connecting admin so their Settings grant is not invalidated.
        // Only safe when the same Google account is used for both — otherwise the
        // admin's personal Calendar token would be silently replaced with one for
        // a different account (or lacking Calendar scope). Skip when the connecting
        // account can't be confirmed to match, or when they have no personal token
        // (do not invent a Drive-only grant).
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(settings.GoogleCalendarEmail)
            || !string.Equals(email, settings.GoogleCalendarEmail, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Skipped mirroring App Drive token onto admin {UserId}'s personal Calendar grant: connecting account does not match their linked Calendar account.",
                userId);
            return;
        }

        settings.GoogleRefreshToken = encrypted;
        settings.GoogleDriveGranted = true;
    }

    private async Task UpsertSettingAsync(string key, string value, string description)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row == null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value, Description = description });
            return;
        }

        row.Value = value;
        if (string.IsNullOrEmpty(row.Description))
            row.Description = description;
    }
}


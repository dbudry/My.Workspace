using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.Functions.Helpers;
using My.Shared.Constants;
using My.Shared.Dtos.GoogleCalendar;
using My.Shared.Rules;
using My.Shared.Validation;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Repository;
using My.Functions.Services;

namespace My.Functions
{
    public class GoogleDriveFunction
    {
        private readonly IRepository<UserSettings> settingsRepository;
        private readonly ApplicationDbContext dbContext;
        private readonly GoogleDriveService drive;
        private readonly GoogleTokenEncryptor encryptor;
        private readonly IMemoryCache cache;
        private readonly ILogger<GoogleDriveFunction> logger;
        private readonly RedirectUriQueryValidator redirectUriValidator;
        private readonly IValidator<GoogleCalendarCallbackDto> callbackValidator;

        public GoogleDriveFunction(
            IRepositoryFactory repositoryFactory,
            ApplicationDbContext dbContext,
            GoogleDriveService drive,
            GoogleTokenEncryptor encryptor,
            IMemoryCache cache,
            ILogger<GoogleDriveFunction> logger,
            RedirectUriQueryValidator redirectUriValidator,
            IValidator<GoogleCalendarCallbackDto> callbackValidator)
        {
            settingsRepository = repositoryFactory.GetRepository<UserSettings>();
            this.dbContext = dbContext;
            this.drive = drive;
            this.encryptor = encryptor;
            this.cache = cache;
            this.logger = logger;
            this.redirectUriValidator = redirectUriValidator;
            this.callbackValidator = callbackValidator;
        }

        [Function("GetGoogleDriveAuthUrl")]
        public async Task<IActionResult> GetAuthUrlAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "googledrive/authurl")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            var userId = principal.FindFirstValue(Constants.Claims.UserId);
            if (string.IsNullOrEmpty(userId))
                return new UnauthorizedResult();

            if (!drive.IsConfigured)
                return new ObjectResult("Google Drive integration is not configured on the server.") { StatusCode = 503 };

            var redirectUri = req.Query["redirectUri"];
            if (RequestValidator.BadRequestIfInvalid(redirectUriValidator, redirectUri) is { } redirectError)
                return redirectError;

            var domains = await AuthDomainSettingsLoader.ResolveAsync(dbContext, cache);
            var hostedDomain = GoogleIdentityRules.GetSingleHostedDomainHint(domains);
            var url = drive.BuildAuthorizationUrl(redirectUri!, state: userId, hostedDomain);
            return new OkObjectResult(new { url });
        }

        [Function("CompleteGoogleDriveCallback")]
        public async Task<IActionResult> CompleteCallbackAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "googledrive/callback")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            var userId = principal.FindFirstValue(Constants.Claims.UserId);
            if (string.IsNullOrEmpty(userId))
                return new UnauthorizedResult();

            var (body, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, callbackValidator);
            if (validationError != null)
                return validationError;

            try
            {
                var (refresh, email) = await drive.ExchangeCodeAsync(body!.Code!, body.RedirectUri!);
                var settings = (await settingsRepository.Get(s => s.UserId == userId)).FirstOrDefault()
                               ?? await InsertNewSettingsAsync(userId);

                if (!string.IsNullOrEmpty(refresh))
                    settings.GoogleRefreshToken = encryptor.Encrypt(refresh);
                else if (string.IsNullOrEmpty(settings.GoogleRefreshToken))
                    return new BadRequestObjectResult(ApiErrorMessages.GoogleDriveConnectFailed);

                settings.GoogleDriveGranted = true;
                if (!string.IsNullOrEmpty(email) && string.IsNullOrEmpty(settings.GoogleCalendarEmail))
                    settings.GoogleCalendarEmail = email;
                await settingsRepository.Update(settings);

                return new OkObjectResult(new { connected = true, email });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Google Drive callback failed for {UserId}.", userId);
                return new BadRequestObjectResult(ApiErrorMessages.GoogleDriveConnectFailed);
            }
        }

        private async Task<UserSettings> InsertNewSettingsAsync(string userId)
        {
            var s = new UserSettings { UserId = userId };
            try
            {
                await settingsRepository.Insert(s);
            }
            catch (DbUpdateException)
            {
                var reloaded = await settingsRepository.Get(x => x.UserId == userId);
                s = reloaded.FirstOrDefault() ?? s;
            }
            return s;
        }
    }
}

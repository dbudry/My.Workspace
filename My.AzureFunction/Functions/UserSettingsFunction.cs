using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using My.Shared.Constants;
using My.Shared.Dtos.UserSettings;
using My.DAL.Models;
using My.DAL.Repository;
using My.Functions.Helpers;

namespace My.Functions
{
    public class UserSettingsFunctions
    {
        private readonly IRepository<UserSettings> settingsRepository;
        private readonly AppMapper mapper;
        private readonly IValidator<UpdateUserSettingsDto> updateValidator;

        public UserSettingsFunctions(
            IRepositoryFactory repositoryFactory,
            AppMapper mapper,
            IValidator<UpdateUserSettingsDto> updateValidator)
        {
            this.mapper = mapper;
            this.updateValidator = updateValidator;
            settingsRepository = repositoryFactory.GetRepository<UserSettings>();
        }

        [Function("GetUserSettings")]
        public async Task<IActionResult> GetUserSettingsAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "usersettings")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            var userId = principal.FindFirstValue(Constants.Claims.UserId);
            if (string.IsNullOrEmpty(userId))
                return new UnauthorizedResult();

            var results = await settingsRepository.Get(s => s.UserId == userId);
            var settings = results.FirstOrDefault();

            if (settings == null)
            {
                settings = new UserSettings { UserId = userId };
                try
                {
                    await settingsRepository.Insert(settings);
                }
                catch (DbUpdateException)
                {
                    // Race condition: another concurrent request (common on first login when
                    // dashboard + provision + nav all fire GetUserSettings at the same time)
                    // already created the row. Reload it instead of failing with
                    // DbUpdateConcurrencyException / unique violation.
                    var reloaded = await settingsRepository.Get(s => s.UserId == userId);
                    settings = reloaded.FirstOrDefault();
                    if (settings == null)
                        throw; // something else went wrong
                }
            }

            return new OkObjectResult(mapper.UserSettingsToDto(settings));
        }

        [Function("UpdateUserSettings")]
        public async Task<IActionResult> UpdateUserSettingsAsync([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "usersettings")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            var userId = principal.FindFirstValue(Constants.Claims.UserId);
            if (string.IsNullOrEmpty(userId))
                return new UnauthorizedResult();

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, updateValidator);
            if (validationError != null)
                return validationError;

            var results = await settingsRepository.Get(s => s.UserId == userId);
            var settings = results.FirstOrDefault();

            if (settings == null)
            {
                settings = new UserSettings { UserId = userId };
                try
                {
                    await settingsRepository.Insert(settings);
                }
                catch (DbUpdateException)
                {
                    // Same race protection as in GET
                    var reloaded = await settingsRepository.Get(s => s.UserId == userId);
                    settings = reloaded.FirstOrDefault();
                    if (settings == null)
                        throw;
                }
            }

            mapper.UpdateUserSettingsFromDto(dto!, settings);

            // Favorites list is not handled by the Mapperly partial (ignored to avoid json column issues),
            // so we map it manually here.
            settings.FavoriteIntranetPageIdsJson = dto!.FavoriteIntranetPageIds == null || dto.FavoriteIntranetPageIds.Count == 0
                ? null
                : System.Text.Json.JsonSerializer.Serialize(dto.FavoriteIntranetPageIds);

            await settingsRepository.Update(settings);

            return new OkObjectResult(mapper.UserSettingsToDto(settings));
        }
    }
}

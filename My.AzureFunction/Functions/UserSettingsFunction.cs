using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using My.Shared.Constants;
using My.Shared.Dtos.Expenses;
using My.Shared.Dtos.UserSettings;
using My.Shared.Rules;
using My.DAL.Models;
using My.DAL.Repository;
using My.Functions.Authorization;
using My.Functions.Helpers;

namespace My.Functions
{
    public class UserSettingsFunctions
    {
        private readonly IRepository<UserSettings> settingsRepository;
        private readonly IRepository<ExpenseReport> reportsRepository;
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
            reportsRepository = repositoryFactory.GetRepository<ExpenseReport>();
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
            await SyncDraftExpenseAddressesAsync(userId, settings.ExpenseHomeAddress);

            return new OkObjectResult(mapper.UserSettingsToDto(settings));
        }

        private async Task SyncDraftExpenseAddressesAsync(string userId, string? address)
        {
            var drafts = await reportsRepository.Get(r =>
                r.UserId == userId && r.Status == ExpenseStatusRules.Draft);
            foreach (var report in drafts)
            {
                report.AddressSnapshot = address;
                await reportsRepository.Update(report);
            }
        }

        [Function("GetExpenseSignatureMedia")]
        public async Task<IActionResult> GetExpenseSignatureMediaAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/signature/media")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth)
                return unauth;

            var results = await settingsRepository.Get(s => s.UserId == userId);
            var settings = results.FirstOrDefault();
            if (settings?.ExpenseSignature == null || settings.ExpenseSignature.Length == 0)
                return new NotFoundObjectResult("No signature.");

            return new FileContentResult(
                settings.ExpenseSignature,
                string.IsNullOrWhiteSpace(settings.ExpenseSignatureMime) ? "image/png" : settings.ExpenseSignatureMime);
        }

        [Function("PutExpenseSignature")]
        public async Task<IActionResult> PutExpenseSignatureAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "expenses/signature")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth)
                return unauth;

            UploadExpenseSignatureDto? body;
            try
            {
                body = await System.Text.Json.JsonSerializer.DeserializeAsync<UploadExpenseSignatureDto>(
                    req.Body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return new BadRequestObjectResult("Invalid signature payload.");
            }

            if (body == null || string.IsNullOrWhiteSpace(body.ContentBase64))
                return new BadRequestObjectResult("Signature image is required.");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(body.ContentBase64);
            }
            catch
            {
                return new BadRequestObjectResult("Signature is not valid base64.");
            }

            var mime = ExpenseSignatureRules.NormalizeMime(body.MimeType, body.FileName);
            if (!ExpenseSignatureRules.TryValidate(body.FileName, mime, bytes.Length, out var error))
                return new BadRequestObjectResult(error);

            var results = await settingsRepository.Get(s => s.UserId == userId);
            var settings = results.FirstOrDefault();
            if (settings == null)
            {
                settings = new UserSettings { UserId = userId };
                await settingsRepository.Insert(settings);
            }

            settings.ExpenseSignature = bytes;
            settings.ExpenseSignatureMime = mime;
            await settingsRepository.Update(settings);
            return new OkObjectResult(mapper.UserSettingsToDto(settings));
        }

        [Function("DeleteExpenseSignature")]
        public async Task<IActionResult> DeleteExpenseSignatureAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "expenses/signature")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedExpenses(principal, out var userId) is IActionResult unauth)
                return unauth;

            var results = await settingsRepository.Get(s => s.UserId == userId);
            var settings = results.FirstOrDefault();
            if (settings == null)
                return new OkResult();

            settings.ExpenseSignature = null;
            settings.ExpenseSignatureMime = null;
            await settingsRepository.Update(settings);
            return new OkObjectResult(mapper.UserSettingsToDto(settings));
        }
    }
}

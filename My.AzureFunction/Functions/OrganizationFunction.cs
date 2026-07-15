using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.Shared.Constants;
using My.Shared.Dtos.Organization;
using My.Shared.Dtos.Paging;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Models.Paging;
using My.DAL.Repository;
using My.Functions.Authorization;
using My.Functions.Helpers;

namespace My.Functions
{
    public class OrganizationFunctions
    {
        private readonly IRepository<Organization> organizationRepository;
        private readonly IRepository<AppSetting> appSettingRepository;
        private readonly ApplicationDbContext dbContext;
        private readonly AppMapper mapper;
        private readonly ILogger<OrganizationFunctions> logger;
        private readonly IValidator<CreateOrganizationDto> createValidator;
        private readonly IValidator<UpdateOrganizationDto> updateValidator;

        public OrganizationFunctions(
            IRepositoryFactory repositoryFactory,
            ApplicationDbContext dbContext,
            AppMapper mapper,
            ILogger<OrganizationFunctions> logger,
            IValidator<CreateOrganizationDto> createValidator,
            IValidator<UpdateOrganizationDto> updateValidator)
        {
            this.mapper = mapper;
            this.logger = logger;
            this.createValidator = createValidator;
            this.updateValidator = updateValidator;
            this.dbContext = dbContext;
            organizationRepository = repositoryFactory.GetRepository<Organization>();
            appSettingRepository = repositoryFactory.GetRepository<AppSetting>();
        }

        [Function("GetOrganizations")]
        public async Task<IActionResult> GetOrganizationsAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organizations")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var listQuery = HttpListQueryParser.ParseListQuery(req);
            bool summary = bool.TryParse(req.Query["summary"], out var s) && s;

            return new OkObjectResult(await QueryOrganizationsAsync(
                HttpListQueryParser.ToPagingParameters(listQuery),
                listQuery.IncludeArchived,
                listQuery.IncludeInactive,
                summary));
        }

        /// <summary>Lightweight typeahead for pickers — one paged, searchable round trip.</summary>
        [Function("LookupOrganizations")]
        public async Task<IActionResult> LookupOrganizationsAsync(
            // Single-segment route on purpose — "organizations/lookup" deploys but some hosts
            // silently 404 nested lookup paths (same class of issue as admin/logs → applogs).
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organizationlookup")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var listQuery = HttpListQueryParser.ParseListQuery(req);
            listQuery.IncludeArchived = true;
            listQuery.IncludeInactive = true;
            if (listQuery.PageSize > 25)
                listQuery.PageSize = 25;

            return new OkObjectResult(await QueryOrganizationsAsync(
                HttpListQueryParser.ToPagingParameters(listQuery),
                includeArchived: true,
                includeInactive: true,
                summary: true));
        }

        private async Task<PagedResponse<OrganizationDto>> QueryOrganizationsAsync(
            PagingParameters parameters,
            bool includeArchived,
            bool includeInactive,
            bool summary)
        {
            var filter = OrganizationListFilters.Build(includeArchived, includeInactive, parameters.Search);
            var orderBy = OrganizationListFilters.OrderByName(parameters.SortDescending);

            var includes = summary ? "" : "Departments";
            var pagedList = await organizationRepository.GetPaged(
                parameters,
                filter: filter,
                orderBy: orderBy,
                includeProperties: includes);

            if (!summary)
            {
                foreach (var org in pagedList)
                {
                    org.Departments = org.Departments?
                        .Where(d =>
                            (!d.IsArchived && (d.IsActive || includeInactive))
                            || (d.IsArchived && includeArchived))
                        .ToList();
                }
            }

            IReadOnlyDictionary<string, int> contactCounts = summary
                ? new Dictionary<string, int>()
                : await LoadOrgLevelContactCountsAsync(pagedList.Select(o => o.OrganizationId).ToList());

            var dtos = pagedList.Select(o => summary
                ? mapper.OrganizationToSummaryDto(o)
                : mapper.OrganizationToListDto(o, contactCounts.GetValueOrDefault(o.OrganizationId))).ToList();

            return new PagedResponse<OrganizationDto>
            {
                Items = dtos,
                TotalCount = pagedList.TotalCount,
                PageSize = pagedList.PageSize,
                CurrentPage = pagedList.CurrentPage,
                TotalPages = pagedList.TotalPages,
                HasNext = pagedList.HasNext,
                HasPrevious = pagedList.HasPrevious
            };
        }

        private async Task<IReadOnlyDictionary<string, int>> LoadOrgLevelContactCountsAsync(
            IReadOnlyList<string> organizationIds)
        {
            if (organizationIds.Count == 0)
                return new Dictionary<string, int>();

            return await dbContext.Contacts
                .AsNoTracking()
                .Where(c => organizationIds.Contains(c.OrganizationId!) && c.DepartmentId == null)
                .GroupBy(c => c.OrganizationId!)
                .Select(g => new { OrganizationId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.OrganizationId, x => x.Count);
        }

        [Function("GetOrganization")]
        public async Task<IActionResult> GetOrganizationAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organizations/{id}")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            bool includeArchived = bool.TryParse(req.Query["includeArchived"], out var ia) && ia;

            var org = await organizationRepository.GetByIdInclude(o => o.OrganizationId == id, "Contacts,Departments,Departments.Contacts");
            if (org == null)
                return new NotFoundObjectResult("Organization not found!");

            if (!includeArchived)
                org.Departments = org.Departments?.Where(d => !d.IsArchived).ToList();

            return new OkObjectResult(mapper.OrganizationToDto(org));
        }

        [Function("CreateOrganization")]
        public async Task<IActionResult> CreateOrganizationAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "organizations")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            // Mutation requires Manager+. GET endpoints remain open to any Tyme user.
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, createValidator);
            if (validationError != null)
                return validationError;

            var org = new Organization
            {
                Name = dto!.Name,
                Address = dto.Address,
                City = dto.City,
                State = dto.State,
                PostalCode = dto.PostalCode,
                Country = dto.Country,
                Note = dto.Note,
                Color = string.IsNullOrEmpty(dto.Color) ? PickRandomColor() : dto.Color
            };

            await organizationRepository.Insert(org);
            return new OkObjectResult(mapper.OrganizationToDto(org));
        }

        private static readonly string[] DefaultOrganizationColors =
        {
            "#1976d2", "#388e3c", "#e64a19", "#7b1fa2", "#00838f",
            "#c62828", "#f9a825", "#4527a0", "#00695c", "#ad1457"
        };

        private static string PickRandomColor() =>
            DefaultOrganizationColors[Random.Shared.Next(DefaultOrganizationColors.Length)];

        [Function("UpdateOrganization")]
        public async Task<IActionResult> UpdateOrganizationAsync([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "organizations")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, updateValidator);
            if (validationError != null)
                return validationError;

            var org = await organizationRepository.GetById(dto!.OrganizationId);
            if (org == null)
                return new NotFoundObjectResult("Organization not found!");

            org.Name = dto.Name;
            org.Address = dto.Address;
            org.City = dto.City;
            org.State = dto.State;
            org.PostalCode = dto.PostalCode;
            org.Country = dto.Country;
            org.Note = dto.Note;
            if (!string.IsNullOrEmpty(dto.Color))
                org.Color = dto.Color;

            await organizationRepository.Update(org);
            return new OkObjectResult(mapper.OrganizationToDto(org));
        }

        [Function("SetActiveOrganization")]
        public async Task<IActionResult> SetActiveOrganizationAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "organizations/{id}/setactive")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var org = await organizationRepository.GetById(id);
            if (org == null)
                return new NotFoundObjectResult("Organization not found!");

            org.IsActive = !org.IsActive;
            await organizationRepository.Update(org);

            logger.LogInformation("Organization {Id} IsActive set to {IsActive}", id, org.IsActive);
            return new OkObjectResult(new { org.IsActive });
        }

        [Function("ArchiveOrganization")]
        public async Task<IActionResult> ArchiveOrganizationAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "organizations/{id}/archive")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var org = await organizationRepository.GetById(id);
            if (org == null)
                return new NotFoundObjectResult("Organization not found!");

            org.IsArchived = !org.IsArchived;
            if (org.IsArchived)
                org.IsActive = false;

            await organizationRepository.Update(org);
            logger.LogInformation("Organization {Id} IsArchived set to {IsArchived}", id, org.IsArchived);
            return new OkObjectResult(new { org.IsArchived, org.IsActive });
        }

        [Function("DeleteOrganization")]
        public async Task<IActionResult> DeleteOrganizationAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "organizations/{id}")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var setting = await appSettingRepository.GetById(Constants.SettingKeys.AllowOrganizationDelete);
            if (setting == null || !bool.TryParse(setting.Value, out var allowed) || !allowed)
                return new BadRequestObjectResult("Organization deletion is not enabled in App Settings.");

            var org = await organizationRepository.GetById(id);
            if (org == null)
                return new NotFoundObjectResult("Organization not found!");

            await organizationRepository.Delete(org);
            logger.LogInformation("Organization {Id} was deleted.", id);
            return new NoContentResult();
        }
    }
}

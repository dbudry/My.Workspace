using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.Shared.Constants;
using My.Shared.Dtos.Department;
using My.Shared.Dtos.Organization;
using My.Shared.Dtos.Contact;
using My.DAL.Models;
using My.DAL.Repository;
using My.Functions.Authorization;
using My.Functions.Helpers;

namespace My.Functions
{
    public class DepartmentFunctions
    {
        private readonly IRepository<Department> departmentRepository;
        private readonly IRepository<Organization> organizationRepository;
        private readonly IRepository<AppSetting> appSettingRepository;
        private readonly AppMapper mapper;
        private readonly ILogger<DepartmentFunctions> logger;
        private readonly IValidator<CreateDepartmentDto> createValidator;
        private readonly IValidator<UpdateDepartmentDto> updateValidator;

        public DepartmentFunctions(
            IRepositoryFactory repositoryFactory,
            AppMapper mapper,
            ILogger<DepartmentFunctions> logger,
            IValidator<CreateDepartmentDto> createValidator,
            IValidator<UpdateDepartmentDto> updateValidator)
        {
            this.mapper = mapper;
            this.logger = logger;
            this.createValidator = createValidator;
            this.updateValidator = updateValidator;
            departmentRepository = repositoryFactory.GetRepository<Department>();
            organizationRepository = repositoryFactory.GetRepository<Organization>();
            appSettingRepository = repositoryFactory.GetRepository<AppSetting>();
        }

        [Function("GetDepartments")]
        public async Task<IActionResult> GetDepartmentsAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var orgId = req.Query["organizationId"];
            bool includeArchived = bool.TryParse(req.Query["includeArchived"], out var ia) && ia;

            var departments = await departmentRepository.Get(
                filter: d => (string.IsNullOrEmpty(orgId) || d.OrganizationId == orgId)
                             && (includeArchived || !d.IsArchived),
                includeProperties: "Contacts");

            var dtos = departments.Select(d => mapper.DepartmentToDto(d));

            return new OkObjectResult(dtos);
        }

        [Function("GetDepartment")]
        public async Task<IActionResult> GetDepartmentAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{id}")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var dept = await departmentRepository.GetByIdInclude(d => d.DepartmentId == id, "Contacts");
            if (dept == null)
                return new NotFoundObjectResult("Department not found!");

            return new OkObjectResult(mapper.DepartmentToDto(dept));
        }

        [Function("CreateDepartment")]
        public async Task<IActionResult> CreateDepartmentAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "departments")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            // Mutation requires Manager+. GET endpoints remain open to any Tyme user.
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, createValidator);
            if (validationError != null)
                return validationError;

            var org = await organizationRepository.GetById(dto!.OrganizationId);
            if (org == null)
                return new NotFoundObjectResult("Organization not found!");

            var dept = new Department
            {
                Name = dto.Name,
                OrganizationId = dto.OrganizationId
            };

            await departmentRepository.Insert(dept);

            return new OkObjectResult(mapper.DepartmentToDto(dept));
        }

        [Function("UpdateDepartment")]
        public async Task<IActionResult> UpdateDepartmentAsync([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "departments")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, updateValidator);
            if (validationError != null)
                return validationError;

            var dept = await departmentRepository.GetById(dto!.DepartmentId);
            if (dept == null)
                return new NotFoundObjectResult("Department not found!");

            dept.Name = dto.Name;

            await departmentRepository.Update(dept);

            return new OkObjectResult(mapper.DepartmentToDto(dept));
        }

        [Function("SetActiveDepartment")]
        public async Task<IActionResult> SetActiveDepartmentAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "departments/{id}/setactive")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var dept = await departmentRepository.GetById(id);
            if (dept == null)
                return new NotFoundObjectResult("Department not found!");

            dept.IsActive = !dept.IsActive;
            await departmentRepository.Update(dept);

            logger.LogInformation("Department {Id} IsActive set to {IsActive}", id, dept.IsActive);
            return new OkObjectResult(new { dept.IsActive });
        }

        [Function("ArchiveDepartment")]
        public async Task<IActionResult> ArchiveDepartmentAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "departments/{id}/archive")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var dept = await departmentRepository.GetById(id);
            if (dept == null)
                return new NotFoundObjectResult("Department not found!");

            dept.IsArchived = !dept.IsArchived;
            if (dept.IsArchived)
                dept.IsActive = false;

            await departmentRepository.Update(dept);
            logger.LogInformation("Department {Id} IsArchived set to {IsArchived}", id, dept.IsArchived);
            return new OkObjectResult(new { dept.IsArchived, dept.IsActive });
        }

        [Function("DeleteDepartment")]
        public async Task<IActionResult> DeleteDepartmentAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "departments/{id}")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var setting = await appSettingRepository.GetById(Constants.SettingKeys.AllowOrganizationDelete);
            if (setting == null || !bool.TryParse(setting.Value, out var allowed) || !allowed)
                return new BadRequestObjectResult("Deletion is not enabled in App Settings.");

            var dept = await departmentRepository.GetById(id);
            if (dept == null)
                return new NotFoundObjectResult("Department not found!");

            await departmentRepository.Delete(dept);
            logger.LogInformation("Department {Id} was deleted.", id);
            return new NoContentResult();
        }
    }
}

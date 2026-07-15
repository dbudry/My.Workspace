using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using My.Shared.Constants;
using My.Shared.Dtos.Contact;
using My.DAL.Models;
using My.DAL.Repository;
using My.Functions.Authorization;
using My.Functions.Helpers;
using My.Shared.Rules;
using My.Shared.Validation;

namespace My.Functions
{
    public class ContactFunctions
    {
        private readonly IRepository<Contact> contactRepository;
        private readonly IRepository<Organization> organizationRepository;
        private readonly IRepository<Department> departmentRepository;
        private readonly IRepository<AppSetting> appSettingRepository;
        private readonly AppMapper mapper;
        private readonly ILogger<ContactFunctions> logger;
        private readonly IValidator<CreateContactDto> createValidator;
        private readonly IValidator<UpdateContactDto> updateValidator;

        public ContactFunctions(
            IRepositoryFactory repositoryFactory,
            AppMapper mapper,
            ILogger<ContactFunctions> logger,
            IValidator<CreateContactDto> createValidator,
            IValidator<UpdateContactDto> updateValidator)
        {
            this.logger = logger;
            this.mapper = mapper;
            this.createValidator = createValidator;
            this.updateValidator = updateValidator;
            contactRepository = repositoryFactory.GetRepository<Contact>();
            organizationRepository = repositoryFactory.GetRepository<Organization>();
            departmentRepository = repositoryFactory.GetRepository<Department>();
            appSettingRepository = repositoryFactory.GetRepository<AppSetting>();
        }

        [Function("GetContacts")]
        public async Task<IActionResult> GetContactsAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "contacts")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var orgId = req.Query["organizationId"];
            var deptId = req.Query["departmentId"];

            var contacts = await contactRepository.Get(
                filter: c =>
                    (string.IsNullOrEmpty(orgId) || c.OrganizationId == orgId) &&
                    (string.IsNullOrEmpty(deptId) || c.DepartmentId == deptId));

            var dtos = contacts.Select(mapper.ContactToDto);

            return new OkObjectResult(dtos);
        }

        [Function("CreateContact")]
        public async Task<IActionResult> CreateContactAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "contacts")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            // Mutation requires Manager+. GET endpoint remains open to any Tyme user.
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var allowedTypes = await ContactTypeSettings.GetAllowedTypesAsync(appSettingRepository);
            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(
                req,
                createValidator,
                ctx =>
                {
                    ctx.RootContextData[ValidationContextKeys.AllowedContactTypes] = allowedTypes;
                    ctx.RootContextData[ValidationContextKeys.ContactTypeRequired] = false;
                });
            if (validationError != null)
                return validationError;

            if (!string.IsNullOrEmpty(dto!.OrganizationId))
            {
                var org = await organizationRepository.GetById(dto.OrganizationId);
                if (org == null)
                    return new NotFoundObjectResult("Organization not found!");
            }

            if (!string.IsNullOrEmpty(dto.DepartmentId))
            {
                var dept = await departmentRepository.GetById(dto.DepartmentId);
                if (dept == null)
                    return new NotFoundObjectResult("Department not found!");
            }

            var requestedType = string.IsNullOrWhiteSpace(dto.ContactType)
                ? ContactTypeRules.DefaultForManualEntry(allowedTypes)
                : dto.ContactType;
            var normalizedType = ContactTypeRules.Normalize(requestedType, allowedTypes);

            var contact = new Contact
            {
                Name = dto.Name,
                Title = dto.Title,
                PhoneNumber = dto.PhoneNumber,
                Email = dto.Email,
                ContactType = normalizedType,
                OrganizationId = dto.OrganizationId,
                DepartmentId = dto.DepartmentId
            };

            await contactRepository.Insert(contact);

            return new OkObjectResult(mapper.ContactToDto(contact));
        }

        [Function("UpdateContact")]
        public async Task<IActionResult> UpdateContactAsync([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "contacts")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var allowedTypes = await ContactTypeSettings.GetAllowedTypesAsync(appSettingRepository);
            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(
                req,
                updateValidator,
                ctx =>
                {
                    ctx.RootContextData[ValidationContextKeys.AllowedContactTypes] = allowedTypes;
                    ctx.RootContextData[ValidationContextKeys.ContactTypeRequired] = true;
                });
            if (validationError != null)
                return validationError;

            var contact = await contactRepository.GetById(dto!.ContactId);
            if (contact == null)
                return new NotFoundObjectResult("Contact not found!");

            var normalizedType = ContactTypeRules.Normalize(dto.ContactType!, allowedTypes);

            contact.Name = dto.Name;
            contact.Title = dto.Title;
            contact.PhoneNumber = dto.PhoneNumber;
            contact.Email = dto.Email;
            contact.ContactType = normalizedType;

            await contactRepository.Update(contact);

            return new OkObjectResult(mapper.ContactToDto(contact));
        }

        [Function("DeleteContact")]
        public async Task<IActionResult> DeleteContactAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "contacts/{id}")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var contact = await contactRepository.GetById(id);
            if (contact == null)
                return new NotFoundObjectResult("Contact not found!");

            await contactRepository.Delete(contact);
            logger.LogInformation("Contact {Id} was deleted.", id);
            return new NoContentResult();
        }
    }
}

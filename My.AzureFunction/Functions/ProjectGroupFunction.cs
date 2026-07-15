using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.Json;
using My.Shared.Constants;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.ProjectGroup;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Models.Paging;
using My.DAL.Repository;
using My.Functions.Authorization;
using My.Functions.Helpers;

namespace My.Functions
{
    public class ProjectGroupFunctions
    {
        private readonly IRepository<ProjectGroup> projectGroupRepository;
        private readonly IRepository<Project> projectRepository;
        private readonly ApplicationDbContext dbContext;
        private readonly AppMapper mapper;
        private readonly ILogger<ProjectGroupFunctions> logger;
        private readonly IValidator<CreateProjectGroupDto> createValidator;
        private readonly IValidator<UpdateProjectGroupDto> updateValidator;

        public ProjectGroupFunctions(
            IRepositoryFactory repositoryFactory,
            ApplicationDbContext dbContext,
            AppMapper mapper,
            ILogger<ProjectGroupFunctions> logger,
            IValidator<CreateProjectGroupDto> createValidator,
            IValidator<UpdateProjectGroupDto> updateValidator)
        {
            this.dbContext = dbContext;
            this.mapper = mapper;
            this.logger = logger;
            this.createValidator = createValidator;
            this.updateValidator = updateValidator;
            projectGroupRepository = repositoryFactory.GetRepository<ProjectGroup>();
            projectRepository = repositoryFactory.GetRepository<Project>();
        }

        [Function("GetProjectGroups")]
        public async Task<IActionResult> GetProjectGroupsAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "projectgroups")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            // Project groups are workspace-shared — no UserId filter.
            var groups = await projectGroupRepository.Get();
            var dtos = mapper.ProjectGroupsToDtos(groups);
            return new OkObjectResult(dtos);
        }

        [Function("GetProjectGroup")]
        public async Task<IActionResult> GetProjectGroupAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "projectgroups/{id}")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth) return unauth;

            var group = await projectGroupRepository.GetById(id);
            if (group == null)
                return new NotFoundObjectResult("Project group not found!");

            return new OkObjectResult(mapper.ProjectGroupToDto(group));
        }

        [Function("CreateProjectGroup")]
        public async Task<IActionResult> CreateProjectGroupAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "projectgroups")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            // Mutation requires Manager+. GET endpoints remain open to any Tyme user.
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, createValidator);
            if (validationError != null)
                return validationError;

            var newGroup = mapper.DtoToProjectGroup(dto!);
            await projectGroupRepository.Insert(newGroup);
            return new OkObjectResult(mapper.ProjectGroupToDto(newGroup));
        }

        [Function("DeleteProjectGroup")]
        public async Task<IActionResult> DeleteProjectGroupAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "projectgroups/{id}")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var groupToDelete = await projectGroupRepository.GetById(id);
            if (groupToDelete == null)
                return new NotFoundObjectResult("Project group not found!");

            // Clear the group reference from all associated projects (workspace-shared,
            // no per-user filter) in a single UPDATE rather than one round-trip per project.
            await dbContext.Projects
                .Where(p => p.ProjectGroupId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ProjectGroupId, (string?)null));

            await projectGroupRepository.Delete(groupToDelete);
            logger.LogInformation("Project group {Id} '{Name}' was deleted.", groupToDelete.ProjectGroupId, groupToDelete.Name);
            return new NoContentResult();
        }

        [Function("UpdateProjectGroup")]
        public async Task<IActionResult> UpdateProjectGroupAsync([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "projectgroups")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (AuthGates.RequireScopedTyme(principal, out var userId, Constants.Roles.Manager) is IActionResult unauth) return unauth;

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, updateValidator);
            if (validationError != null)
                return validationError;

            var foundGroup = await projectGroupRepository.GetById(dto!.ProjectGroupId);
            if (foundGroup == null)
                return new NotFoundObjectResult("Project group not found!");

            mapper.UpdateProjectGroupFromDto(dto, foundGroup);
            await projectGroupRepository.Update(foundGroup);
            return new OkObjectResult(mapper.ProjectGroupToDto(foundGroup));
        }
    }
}

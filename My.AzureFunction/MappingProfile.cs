using Riok.Mapperly.Abstractions;
using My.Shared.Dtos;
using My.Shared.Dtos.Contact;
using My.Shared.Dtos.Organization;
using My.Shared.Dtos.Project;
using My.Shared.Dtos.ProjectGroup;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Dtos.UserSettings;
using My.Shared.Rules;
using My.DAL.Models;

namespace My.Functions
{
    [Mapper]
    public partial class AppMapper
    {
        // TrackedTask
        [MapperIgnoreSource(nameof(TrackedTask.IsBillable))]
        [MapperIgnoreSource(nameof(TrackedTask.GoogleEventId))]
        [MapperIgnoreSource(nameof(TrackedTask.TeamAvailabilityEventId))]
        [MapperIgnoreSource(nameof(TrackedTask.StopwatchItem))]
        [MapperIgnoreSource(nameof(TrackedTask.User))]
        [MapperIgnoreTarget(nameof(TrackedTaskDto.IsMonthSubmitted))]
        [MapperIgnoreTarget(nameof(TrackedTaskDto.IsManagerAdjusted))]
        [MapperIgnoreTarget(nameof(TrackedTaskDto.AdjustmentKind))]
        [MapperIgnoreTarget(nameof(TrackedTaskDto.ManagerAdjustment))]
        [MapperIgnoreTarget(nameof(TrackedTaskDto.User))]
        public partial TrackedTaskDto TrackedTaskToDto(TrackedTask task);

        [MapperIgnoreSource(nameof(CreateTrackedTaskDto.Project))]
        [MapperIgnoreSource(nameof(CreateTrackedTaskDto.EndDate))]
        [MapperIgnoreTarget(nameof(TrackedTask.TaskId))]
        [MapperIgnoreTarget(nameof(TrackedTask.EndDate))]
        [MapperIgnoreTarget(nameof(TrackedTask.IsBillable))]
        [MapperIgnoreTarget(nameof(TrackedTask.UserId))]
        [MapperIgnoreTarget(nameof(TrackedTask.User))]
        [MapperIgnoreTarget(nameof(TrackedTask.Project))]
        [MapperIgnoreTarget(nameof(TrackedTask.GoogleEventId))]
        [MapperIgnoreTarget(nameof(TrackedTask.TeamAvailabilityEventId))]
        [MapperIgnoreTarget(nameof(TrackedTask.StopwatchItemId))]
        [MapperIgnoreTarget(nameof(TrackedTask.StopwatchItem))]
        public partial TrackedTask DtoToTrackedTask(CreateTrackedTaskDto dto);

        [MapperIgnoreSource(nameof(UpdateTrackedTaskDto.Project))]
        [MapperIgnoreSource(nameof(UpdateTrackedTaskDto.Duration))]
        [MapperIgnoreTarget(nameof(TrackedTask.Duration))]
        [MapperIgnoreTarget(nameof(TrackedTask.IsBillable))]
        [MapperIgnoreTarget(nameof(TrackedTask.UserId))]
        [MapperIgnoreTarget(nameof(TrackedTask.User))]
        [MapperIgnoreTarget(nameof(TrackedTask.Project))]
        [MapperIgnoreTarget(nameof(TrackedTask.GoogleEventId))]
        [MapperIgnoreTarget(nameof(TrackedTask.TeamAvailabilityEventId))]
        [MapperIgnoreTarget(nameof(TrackedTask.StopwatchItemId))]
        [MapperIgnoreTarget(nameof(TrackedTask.StopwatchItem))]
        public partial void UpdateTrackedTaskFromDto(UpdateTrackedTaskDto dto, TrackedTask target);

        public IEnumerable<TrackedTaskDto> TrackedTasksToDtos(IEnumerable<TrackedTask> tasks)
            => tasks.Select(TrackedTaskToDto);

        // Project
        [MapProperty(nameof(Project.Organization) + "." + nameof(Organization.Name), nameof(ProjectDto.OrganizationName))]
        [MapProperty(nameof(Project.Organization) + "." + nameof(Organization.Color), nameof(ProjectDto.OrganizationColor))]
        [MapProperty(nameof(Project.Department) + "." + nameof(Department.Name), nameof(ProjectDto.DepartmentName))]
        [MapProperty(nameof(Project.ProjectGroup) + "." + nameof(DAL.Models.ProjectGroup.Name), nameof(ProjectDto.ProjectGroupName))]
        [MapProperty(nameof(Project.ProjectGroup) + "." + nameof(DAL.Models.ProjectGroup.Color), nameof(ProjectDto.ProjectGroupColor))]
        [MapperIgnoreSource(nameof(Project.TrackedTasks))]
        public partial ProjectDto ProjectToDto(Project project);

        [MapperIgnoreTarget(nameof(Project.ProjectId))]
        [MapperIgnoreTarget(nameof(Project.Organization))]
        [MapperIgnoreTarget(nameof(Project.Department))]
        [MapperIgnoreTarget(nameof(Project.ProjectGroup))]
        [MapperIgnoreTarget(nameof(Project.TrackedTasks))]
        [MapperIgnoreTarget(nameof(Project.IsActive))]
        [MapperIgnoreTarget(nameof(Project.IsArchived))]
        public partial Project DtoToProject(CreateProjectDto dto);

        [MapperIgnoreTarget(nameof(Project.Organization))]
        [MapperIgnoreTarget(nameof(Project.Department))]
        [MapperIgnoreTarget(nameof(Project.ProjectGroup))]
        [MapperIgnoreTarget(nameof(Project.TrackedTasks))]
        [MapperIgnoreTarget(nameof(Project.IsActive))]
        [MapperIgnoreTarget(nameof(Project.IsArchived))]
        public partial void UpdateProjectFromDto(UpdateProjectDto dto, Project target);

        public IEnumerable<ProjectDto> ProjectsToDtos(IEnumerable<Project> projects)
            => projects.Select(ProjectToDto);

        // ProjectGroup
        [MapperIgnoreSource(nameof(DAL.Models.ProjectGroup.Projects))]
        public partial ProjectGroupDto ProjectGroupToDto(DAL.Models.ProjectGroup group);

        [MapperIgnoreTarget(nameof(DAL.Models.ProjectGroup.ProjectGroupId))]
        [MapperIgnoreTarget(nameof(DAL.Models.ProjectGroup.Projects))]
        public partial DAL.Models.ProjectGroup DtoToProjectGroup(CreateProjectGroupDto dto);

        [MapperIgnoreTarget(nameof(DAL.Models.ProjectGroup.Projects))]
        public partial void UpdateProjectGroupFromDto(UpdateProjectGroupDto dto, DAL.Models.ProjectGroup target);

        public IEnumerable<ProjectGroupDto> ProjectGroupsToDtos(IEnumerable<DAL.Models.ProjectGroup> groups)
            => groups.Select(ProjectGroupToDto);

        // Contact
        public ContactDto ContactToDto(Contact contact)
        {
            return new ContactDto
            {
                ContactId = contact.ContactId,
                Name = contact.Name,
                Title = contact.Title,
                PhoneNumber = contact.PhoneNumber,
                Email = contact.Email,
                ContactType = contact.ContactType,
                OrganizationId = contact.OrganizationId,
                DepartmentId = contact.DepartmentId
            };
        }

        // Department
        public DepartmentDto DepartmentToDto(Department dept)
        {
            return new DepartmentDto
            {
                DepartmentId = dept.DepartmentId,
                Name = dept.Name,
                OrganizationId = dept.OrganizationId,
                IsActive = dept.IsActive,
                IsArchived = dept.IsArchived,
                Contacts = dept.Contacts?.Select(ContactToDto).ToList()
            };
        }

        // Organization
        public OrganizationDto OrganizationToDto(Organization org)
        {
            return new OrganizationDto
            {
                OrganizationId = org.OrganizationId,
                Name = org.Name,
                Address = org.Address,
                City = org.City,
                State = org.State,
                PostalCode = org.PostalCode,
                Country = org.Country,
                Note = org.Note,
                Color = org.Color,
                IsActive = org.IsActive,
                IsArchived = org.IsArchived,
                ContactCount = org.Contacts?.Count(c => c.DepartmentId == null) ?? 0,
                DepartmentCount = org.Departments?.Count ?? 0,
                Contacts = org.Contacts?.Where(c => c.DepartmentId == null).Select(ContactToDto).ToList(),
                Departments = org.Departments?.Select(DepartmentToDto).ToList()
            };
        }

        /// <summary>Paged list row — departments for sub-rows, no contact hydration.</summary>
        public OrganizationDto OrganizationToListDto(Organization org, int contactCount)
        {
            return new OrganizationDto
            {
                OrganizationId = org.OrganizationId,
                Name = org.Name,
                Address = org.Address,
                City = org.City,
                State = org.State,
                PostalCode = org.PostalCode,
                Country = org.Country,
                Note = org.Note,
                Color = org.Color,
                IsActive = org.IsActive,
                IsArchived = org.IsArchived,
                ContactCount = contactCount,
                DepartmentCount = org.Departments?.Count ?? 0,
                Departments = org.Departments?.Select(DepartmentToListDto).ToList()
            };
        }

        public DepartmentDto DepartmentToListDto(Department dept) => new()
        {
            DepartmentId = dept.DepartmentId,
            Name = dept.Name,
            OrganizationId = dept.OrganizationId,
            IsActive = dept.IsActive,
            IsArchived = dept.IsArchived
        };

        public OrganizationDto OrganizationToSummaryDto(Organization org) => new()
        {
            OrganizationId = org.OrganizationId,
            Name = org.Name,
            Address = org.Address,
            City = org.City,
            State = org.State,
            PostalCode = org.PostalCode,
            Country = org.Country,
            Note = org.Note,
            Color = org.Color,
            IsActive = org.IsActive,
            IsArchived = org.IsArchived
        };

        // UserSettings
        public UserSettingsDto UserSettingsToDto(DAL.Models.UserSettings settings)
        {
            return new UserSettingsDto
            {
                UserSettingsId = settings.UserSettingsId,
                UserId = settings.UserId,
                Use24HourTime = settings.Use24HourTime,
                TimeZone = settings.TimeZone,
                IsGoogleCalendarConnected = !string.IsNullOrEmpty(settings.GoogleRefreshToken)
                                            && !string.IsNullOrEmpty(settings.GoogleCalendarId),
                GoogleCalendarEmail = settings.GoogleCalendarEmail,
                PublishToGoogleCalendar = settings.PublishToGoogleCalendar,
                ImportFromGoogleCalendar = settings.ImportFromGoogleCalendar,
                TymeEventColorId = settings.TymeEventColorId,
                TymeUnmatchedEventColorId = settings.TymeUnmatchedEventColorId,
                ProjectColorSource = (ProjectColorSource)settings.ProjectColorSource,
                FavoriteIntranetPageIds = string.IsNullOrWhiteSpace(settings.FavoriteIntranetPageIdsJson)
                    ? new List<string>()
                    : System.Text.Json.JsonSerializer.Deserialize<List<string>>(settings.FavoriteIntranetPageIdsJson) ?? new List<string>(),
                CalendarBackfillPromptAcknowledged = settings.CalendarBackfillAcknowledgedUtc != null
            };
        }

        [MapperIgnoreTarget(nameof(DAL.Models.UserSettings.UserSettingsId))]
        [MapperIgnoreTarget(nameof(DAL.Models.UserSettings.UserId))]
        [MapperIgnoreTarget(nameof(DAL.Models.UserSettings.User))]
        [MapperIgnoreTarget(nameof(DAL.Models.UserSettings.GoogleRefreshToken))]
        [MapperIgnoreTarget(nameof(DAL.Models.UserSettings.GoogleCalendarId))]
        [MapperIgnoreTarget(nameof(DAL.Models.UserSettings.GoogleCalendarEmail))]
        [MapperIgnoreTarget(nameof(DAL.Models.UserSettings.GoogleChannelId))]
        [MapperIgnoreTarget(nameof(DAL.Models.UserSettings.GoogleChannelToken))]
        [MapperIgnoreTarget(nameof(DAL.Models.UserSettings.GoogleResourceId))]
        [MapperIgnoreTarget(nameof(DAL.Models.UserSettings.GoogleChannelExpiresAt))]
        [MapperIgnoreTarget(nameof(DAL.Models.UserSettings.GoogleSyncToken))]
        [MapperIgnoreTarget(nameof(DAL.Models.UserSettings.CalendarBackfillAcknowledgedUtc))]
        [MapperIgnoreTarget(nameof(DAL.Models.UserSettings.FavoriteIntranetPageIdsJson))]
        [MapperIgnoreSource(nameof(UpdateUserSettingsDto.FavoriteIntranetPageIds))]
        public partial void UpdateUserSettingsFromDto(UpdateUserSettingsDto dto, DAL.Models.UserSettings target);

        // ApplicationUser
        [MapProperty(nameof(ApplicationUser.UserName), nameof(ApplicationUserDto.Username))]
        [MapperIgnoreSource(nameof(ApplicationUser.LastLoginDate))]
        [MapperIgnoreSource(nameof(ApplicationUser.TrackedTasks))]
        [MapperIgnoreSource(nameof(ApplicationUser.Id))]
        [MapperIgnoreSource(nameof(ApplicationUser.NormalizedUserName))]
        [MapperIgnoreSource(nameof(ApplicationUser.Email))]
        [MapperIgnoreSource(nameof(ApplicationUser.NormalizedEmail))]
        [MapperIgnoreSource(nameof(ApplicationUser.EmailConfirmed))]
        [MapperIgnoreSource(nameof(ApplicationUser.PasswordHash))]
        [MapperIgnoreSource(nameof(ApplicationUser.SecurityStamp))]
        [MapperIgnoreSource(nameof(ApplicationUser.ConcurrencyStamp))]
        [MapperIgnoreSource(nameof(ApplicationUser.PhoneNumber))]
        [MapperIgnoreSource(nameof(ApplicationUser.PhoneNumberConfirmed))]
        [MapperIgnoreSource(nameof(ApplicationUser.TwoFactorEnabled))]
        [MapperIgnoreSource(nameof(ApplicationUser.LockoutEnd))]
        [MapperIgnoreSource(nameof(ApplicationUser.LockoutEnabled))]
        [MapperIgnoreSource(nameof(ApplicationUser.AccessFailedCount))]
        [MapperIgnoreSource(nameof(ApplicationUser.LastSignInAt))]
        [MapperIgnoreSource(nameof(ApplicationUser.OidcSessionInvalidatedAt))]
        public partial ApplicationUserDto UserToDto(ApplicationUser user);
    }
}

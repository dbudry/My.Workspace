using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using My.DAL.Data;
using My.DAL.Models;
using My.Functions.Services;
using FluentValidation;
using My.Functions.Helpers;
using My.Shared.Constants;
using My.Shared.Dtos.User;
using My.Shared.Rules;

namespace My.Functions
{
    public class UserFunctions
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager;
        private readonly ApplicationDbContext _dbContext;
        private readonly IMemoryCache _cache;
        private readonly GoogleCalendarService _googleCalendar;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<UserFunctions> _logger;
        private readonly IValidator<CreateUserDto> _createValidator;
        private readonly IValidator<UpdateUserDto> _updateValidator;

        public UserFunctions(
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager,
            ApplicationDbContext dbContext,
            IMemoryCache cache,
            GoogleCalendarService googleCalendar,
            IHttpClientFactory httpClientFactory,
            ILogger<UserFunctions> logger,
            IValidator<CreateUserDto> createValidator,
            IValidator<UpdateUserDto> updateValidator)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _dbContext = dbContext;
            _cache = cache;
            _googleCalendar = googleCalendar;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _createValidator = createValidator;
            _updateValidator = updateValidator;
        }

        /// <summary>Cache key shape used by AuthMiddleware for the user's role list.</summary>
        private static string RoleCacheKey(string userId) => $"userroles:{userId}";

        /// <summary>Far-future lockout end for inactive/archived users. Avoids DateTimeOffset.MaxValue
        /// (overflows SQL datetimeoffset) and year-9999 edge cases on some hosts.</summary>
        private static readonly DateTimeOffset InactiveLockoutEnd = new(2099, 12, 31, 23, 59, 59, TimeSpan.Zero);

        private static UserDto ToDto(ApplicationUser user, IList<string> roles) => new()
        {
            Id = user.Id,
            Email = user.Email ?? user.UserName ?? string.Empty,
            FirstName = user.FirstName ?? string.Empty,
            LastName = user.LastName ?? string.Empty,
            Roles = roles.ToList(),
            LastLoginDate = user.LastLoginDate,
            IsActive = user.IsActive,
            IsArchived = user.IsArchived
        };

        private static void ApplyInactiveLockout(ApplicationUser user)
        {
            user.IsActive = false;
            user.LockoutEnabled = true;
            user.LockoutEnd = InactiveLockoutEnd;
        }

        private static void ClearInactiveLockout(ApplicationUser user)
        {
            user.IsActive = true;
            user.LockoutEnd = null;
        }

        private async Task<List<string>> GetUserRolesAsync(string userId) =>
            await (from ur in _dbContext.UserRoles
                   where ur.UserId == userId
                   join r in _dbContext.Roles on ur.RoleId equals r.Id
                   select r.Name!).ToListAsync();

        private async Task<IActionResult?> CommitUserAsync(ApplicationUser user, string operation)
        {
            try
            {
                IdentityUserHealing.EnsureStamps(user);
                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    var msg = string.Join(" ", result.Errors.Select(e => e.Description));
                    _logger.LogWarning("{Operation} identity update failed for {UserId}: {Errors}", operation, user.Id, msg);
                    return new BadRequestObjectResult(string.IsNullOrWhiteSpace(msg)
                        ? $"Could not {operation.ToLowerInvariant()}."
                        : msg);
                }

                return null;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "{Operation} database error for {UserId}.", operation, user.Id);
                return new BadRequestObjectResult(
                    "Database error saving user changes. Check application logs for details.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} unexpected error for {UserId}.", operation, user.Id);
                return new BadRequestObjectResult(
                    "Unexpected error saving user changes. Check application logs for details.");
            }
        }

        /// <summary>
        /// Called after Google OIDC login. If no users exist, provisions the caller as Admin.
        /// If the user already exists, returns their profile. Otherwise returns 403.
        /// </summary>
        [Function("ProvisionUser")]
        public async Task<IActionResult> ProvisionAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/provision")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            var googleSub = principal.FindFirstValue(Constants.Claims.UserId);
            var email = principal.FindFirstValue(ClaimTypes.Email);
            var name = principal.FindFirstValue(ClaimTypes.Name);

            if (string.IsNullOrEmpty(googleSub) || string.IsNullOrEmpty(email))
                return new UnauthorizedResult();

            var allowedDomains = await AuthDomainSettingsLoader.ResolveAsync(_dbContext, _cache);
            if (!GoogleIdentityRules.IsAllowedEmail(email, allowedDomains))
            {
                _logger.LogWarning(
                    "Provision rejected for {Email}: email domain not allowed (policy={Policy}).",
                    email,
                    string.IsNullOrWhiteSpace(allowedDomains) ? "(not configured — complete setup wizard)" : allowedDomains);
                return new StatusCodeResult(403);
            }

            // Check if this user already exists
            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                // Block inactive or archived users from logging in
                if (!existingUser.IsActive || existingUser.IsArchived)
                {
                    _logger.LogWarning("Inactive/archived user {Email} attempted login.", email);
                    return new StatusCodeResult(403);
                }

                // Heal stale FirstName/LastName when the stored values look auto-generated
                // (the email itself, or its local-part). Earlier provisioning paths
                // sometimes stuffed the email into FirstName because Google's tokeninfo
                // endpoint (the AuthMiddleware fallback when the Bearer is an opaque
                // access_token) doesn't return a name. Try a real name from the request's
                // Bearer token first; if that doesn't yield one either, leave the existing
                // value alone — better to keep the email guess than overwrite an
                // admin-set name.
                var emailLocal = email.Split('@')[0];
                var firstLooksAuto = string.IsNullOrEmpty(existingUser.FirstName)
                    || existingUser.FirstName.Contains('@')
                    || string.Equals(existingUser.FirstName, email, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existingUser.FirstName, emailLocal, StringComparison.OrdinalIgnoreCase);
                var lastLooksAuto = string.IsNullOrEmpty(existingUser.LastName);
                if (firstLooksAuto && lastLooksAuto)
                {
                    var (healedFirst, healedLast) = await ResolveDisplayNameAsync(name, email, req);
                    if (!string.IsNullOrWhiteSpace(healedFirst))
                    {
                        existingUser.FirstName = healedFirst;
                        existingUser.LastName = healedLast;
                        _logger.LogInformation(
                            "Healed display name for user {Email}: FirstName='{First}' LastName='{Last}'.",
                            email, existingUser.FirstName, existingUser.LastName);
                    }
                }

                // Stamp this as a fresh sign-in. AuthMiddleware compares LastSignInAt
                // against OidcSessionInvalidatedAt to enforce admin-initiated session
                // resets — bumping it here is what flips the comparison after a purge,
                // letting the user back in normally on their next request.
                existingUser.LastSignInAt = DateTimeOffset.UtcNow;
                IdentityUserHealing.EnsureStamps(existingUser);
                await _userManager.UpdateAsync(existingUser);

                var roles = await _userManager.GetRolesAsync(existingUser);

                // Note: We no longer auto-grant module-scoped admin roles (Admin:Tyme, Admin:Intranet)
                // to global Admins on every login. This was a legacy bootstrap convenience.
                // Global Admins must have the scoped roles explicitly assigned if they want
                // module access (or use impersonation). This respects the design where global
                // Admin is only for system setup (Users, AppSettings, Logs, etc.) and does not
                // automatically confer module permissions. Explicit removal of scoped roles
                // will now stick.
                var roleList = roles.ToList();

                return new OkObjectResult(ToDto(existingUser, roleList));
            }

            // If NO users exist at all, this is the first user — make them global Admin
            // PLUS the primary scoped admin roles (Admin:Tyme, Admin:Intranet). This lets the
            // bootstrap user immediately access personal dashboard data, time submissions,
            // the Tyme module, Intranet, etc. without a separate role-assignment step or
            // having to use the impersonation dialog on first run.
            var anyUsers = await _dbContext.ApplicationUsers.AnyAsync();
            if (!anyUsers)
            {
                var (firstName, lastName) = await ResolveDisplayNameAsync(name, email, req);
                var adminUser = new ApplicationUser
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = email,
                    NormalizedUserName = email.ToUpperInvariant(),
                    Email = email,
                    NormalizedEmail = email.ToUpperInvariant(),
                    EmailConfirmed = true,
                    FirstName = !string.IsNullOrWhiteSpace(firstName) ? firstName : "Admin",
                    LastName = lastName,
                    LastLoginDate = DateTimeOffset.UtcNow,
                    LastSignInAt = DateTimeOffset.UtcNow
                };

                var result = await _userManager.CreateAsync(adminUser);
                if (!result.Succeeded)
                {
                    _logger.LogError("Failed to create admin user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
                    return new StatusCodeResult(500);
                }

                var bootstrapRoles = new[]
                {
                    Constants.Roles.Admin,
                    Constants.Roles.Scoped(Constants.Roles.Admin, Constants.Scopes.Tyme),
                    Constants.Roles.Scoped(Constants.Roles.Admin, Constants.Scopes.Intranet)
                };

                var assignedRoles = new List<string>();
                foreach (var roleName in bootstrapRoles)
                {
                    if (!await _roleManager.RoleExistsAsync(roleName))
                    {
                        var desc = roleName.Contains(':')
                            ? $"{roleName.Split(':')[0]}-level role for the {roleName.Split(':')[1]} module."
                            : "Global role.";
                        await _roleManager.CreateAsync(new ApplicationRole { Name = roleName, Description = desc });
                    }

                    var addRes = await _userManager.AddToRoleAsync(adminUser, roleName);
                    if (addRes.Succeeded)
                        assignedRoles.Add(roleName);
                    else
                        _logger.LogWarning("Failed to assign bootstrap role {Role} to first user: {Errors}",
                            roleName, string.Join(", ", addRes.Errors.Select(e => e.Description)));
                }

                _logger.LogInformation("First user {Email} provisioned as {Roles}.", email, string.Join(", ", assignedRoles));

                // First successful admin completes the setup wizard.
                await SetupState.MarkCompletedAsync(_dbContext, _cache);

                return new OkObjectResult(ToDto(adminUser, assignedRoles));
            }

            // User doesn't exist and they're not the first — must be pre-created by admin
            _logger.LogWarning("Unauthorized login attempt from {Email} — user not provisioned by admin.", email);
            return new StatusCodeResult(403);
        }

        /// <summary>
        /// Admin: Get all users. Pass ?includeArchived=true to include archived users.
        /// Global Admin sees everyone; a scoped Admin (e.g. Admin:Tyme) sees only users
        /// whose roles intersect the scopes they administer.
        /// </summary>
        [Function("GetUsers")]
        public async Task<IActionResult> GetUsersAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (!Constants.Roles.IsAnyAdmin(principal))
                return new StatusCodeResult(403);

            var includeArchived = req.Url.Query?.Contains("includeArchived=true", StringComparison.OrdinalIgnoreCase) == true;

            IQueryable<ApplicationUser> query = _dbContext.ApplicationUsers;
            if (!includeArchived)
                query = query.Where(u => !u.IsArchived);

            var users = await query.ToListAsync();
            if (users.Count == 0)
                return new OkObjectResult(Array.Empty<UserDto>());

            // Pre-load (UserId → roles) in a single join instead of calling
            // _userManager.GetRolesAsync per user (which issued one DB round-trip
            // per row — O(n) queries became O(1)).
            var userIds = users.Select(u => u.Id).ToList();
            var roleRows = await (from ur in _dbContext.UserRoles
                                  where userIds.Contains(ur.UserId)
                                  join r in _dbContext.Roles on ur.RoleId equals r.Id
                                  select new { ur.UserId, RoleName = r.Name! })
                                 .ToListAsync();
            var rolesByUser = roleRows
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName).ToList());

            var userDtos = new List<UserDto>(users.Count);
            foreach (var user in users)
            {
                var roles = rolesByUser.TryGetValue(user.Id, out var r) ? r : new List<string>();
                if (!Constants.Roles.IsVisibleTo(principal, roles)) continue;
                userDtos.Add(ToDto(user, roles));
            }

            return new OkObjectResult(userDtos);
        }

        /// <summary>
        /// Admin: Create a new user (pre-provisioning before they log in).
        /// </summary>
        [Function("CreateUser")]
        public async Task<IActionResult> CreateUserAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            // Create is global Admin only — scoped admins shouldn't be able to mint
            // accounts that may sit in scopes other than their own (the role-by-role
            // CanAssignRole check below isn't sufficient because they could create a
            // user with no roles, then expand it later via UpdateUser).
            if (!Constants.Roles.IsGlobalAdmin(principal))
                return new StatusCodeResult(403);

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, _createValidator);
            if (validationError != null)
                return validationError;

            foreach (var role in dto!.Roles)
            {
                if (!Constants.Roles.IsAssignableRole(role))
                    return new BadRequestObjectResult($"Role '{role}' is not assignable.");
                if (!Constants.Roles.CanAssignRole(principal, role))
                    return new StatusCodeResult(403);
            }

            // Check if user already exists
            var existing = await _userManager.FindByEmailAsync(dto.Email);
            if (existing != null)
                return new ConflictObjectResult("A user with this email already exists.");

            var newUser = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = dto.Email,
                NormalizedUserName = dto.Email.ToUpperInvariant(),
                Email = dto.Email,
                NormalizedEmail = dto.Email.ToUpperInvariant(),
                EmailConfirmed = true,
                FirstName = dto.FirstName,
                LastName = dto.LastName
            };

            var result = await _userManager.CreateAsync(newUser);
            if (!result.Succeeded)
            {
                _logger.LogError("Failed to create user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
                return new StatusCodeResult(500);
            }

            // Ensure all roles exist, then assign
            await EnsureRolesExist(dto.Roles);
            await _userManager.AddToRolesAsync(newUser, dto.Roles);
            _logger.LogInformation("Admin created user {Email} with roles {Roles}.", dto.Email, string.Join(", ", dto.Roles));

            return new OkObjectResult(ToDto(newUser, dto.Roles));
        }

        /// <summary>
        /// Admin: Update a user's details and roles.
        /// </summary>
        [Function("UpdateUser")]
        public async Task<IActionResult> UpdateUserAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users")] HttpRequestData req)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (!Constants.Roles.IsAnyAdmin(principal))
                return new StatusCodeResult(403);

            var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, _updateValidator);
            if (validationError != null)
                return validationError;

            var user = await _userManager.FindByIdAsync(dto!.Id);
            if (user == null)
                return new NotFoundObjectResult("User not found.");

            var targetRoles = dto.Roles
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (targetRoles.Count == 0)
                return new BadRequestObjectResult("At least one role is required.");

            foreach (var role in targetRoles)
            {
                if (!Constants.Roles.IsAssignableRole(role))
                    return new BadRequestObjectResult($"Role '{role}' is not assignable.");
                if (!Constants.Roles.CanAssignRole(principal, role))
                    return new StatusCodeResult(403);
            }

            // Caller must have authority over the target's *current* roles AND every role they
            // intend to assign. Without this a scoped admin could "rescue" a user out of their
            // scope, or vice-versa.
            var currentRoles = await _userManager.GetRolesAsync(user);
            if (!Constants.Roles.CanManageUser(principal, currentRoles))
                return new StatusCodeResult(403);

            var emailOwner = await _userManager.FindByEmailAsync(dto.Email);
            if (emailOwner != null && !string.Equals(emailOwner.Id, user.Id, StringComparison.Ordinal))
                return new ConflictObjectResult($"Another user already uses email '{dto.Email}'.");

            var loginOwner = await _userManager.FindByNameAsync(dto.Email);
            if (loginOwner != null && !string.Equals(loginOwner.Id, user.Id, StringComparison.Ordinal))
                return new ConflictObjectResult($"Another user already uses login '{dto.Email}'.");

            user.Email = dto.Email;
            user.NormalizedEmail = dto.Email.ToUpperInvariant();
            user.UserName = dto.Email;
            user.NormalizedUserName = dto.Email.ToUpperInvariant();
            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;

            try
            {
                IdentityUserHealing.EnsureStamps(user);
                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    var msg = string.Join(" ", updateResult.Errors.Select(e => e.Description));
                    _logger.LogWarning("UpdateUser identity update failed for {UserId}: {Errors}", user.Id, msg);
                    return new BadRequestObjectResult(string.IsNullOrWhiteSpace(msg) ? "Could not update user." : msg);
                }

                // Diff roles before mutating — only remove/add what actually changed.
                // Identity churn is wasted DB work and shows up in audit logs.
                var currentSet = currentRoles.ToHashSet(StringComparer.Ordinal);
                var targetSet = targetRoles.ToHashSet(StringComparer.Ordinal);
                var toRemove = currentSet.Except(targetSet).ToList();
                var toAdd = targetSet.Except(currentSet).ToList();

                if (toRemove.Count > 0)
                {
                    var removeResult = await _userManager.RemoveFromRolesAsync(user, toRemove);
                    if (!removeResult.Succeeded)
                    {
                        var msg = string.Join(" ", removeResult.Errors.Select(e => e.Description));
                        _logger.LogWarning("UpdateUser role removal failed for {UserId}: {Errors}", user.Id, msg);
                        return new BadRequestObjectResult(string.IsNullOrWhiteSpace(msg) ? "Could not update user roles." : msg);
                    }
                }

                if (toAdd.Count > 0)
                {
                    await EnsureRolesExist(toAdd);
                    var addResult = await _userManager.AddToRolesAsync(user, toAdd);
                    if (!addResult.Succeeded)
                    {
                        var msg = string.Join(" ", addResult.Errors.Select(e => e.Description));
                        _logger.LogWarning("UpdateUser role assignment failed for {UserId}: {Errors}", user.Id, msg);
                        return new BadRequestObjectResult(string.IsNullOrWhiteSpace(msg) ? "Could not update user roles." : msg);
                    }
                }

                // Bust the auth-middleware role cache so the next request from this user
                // sees the new role set immediately instead of waiting up to 60s for the
                // entry to expire.
                if (toRemove.Count > 0 || toAdd.Count > 0)
                    _cache.Remove(RoleCacheKey(user.Id));

                return new OkObjectResult(ToDto(user, targetRoles));
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "UpdateUser database error for {UserId}.", user.Id);
                return new BadRequestObjectResult(
                    "Database error updating user. This often means the email/login is already used by another account.");
            }
        }

        /// <summary>
        /// Admin: Toggle a user's active status. Inactive users are locked out and cannot log in.
        /// </summary>
        [Function("SetActiveUser")]
        public async Task<IActionResult> SetActiveUserAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/{id}/setactive")] HttpRequestData req, string id)
        {
            try
            {
                var principal = new ClaimsPrincipal(req.Identities);

                if (!Constants.Roles.IsAnyAdmin(principal))
                    return new StatusCodeResult(403);

                if (string.IsNullOrWhiteSpace(id))
                    return new BadRequestObjectResult("User id is required.");

                var user = await _dbContext.ApplicationUsers.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == id);
                if (user == null)
                    return new NotFoundObjectResult("User not found.");

                var roles = await GetUserRolesAsync(id);
                if (!Constants.Roles.CanManageUser(principal, roles))
                    return new StatusCodeResult(403);

                if (user.Email?.Equals(principal.FindFirstValue(ClaimTypes.Email), StringComparison.OrdinalIgnoreCase) == true)
                    return new BadRequestObjectResult("You cannot change your own active status.");

                var newIsActive = !user.IsActive;
                var lockoutEnd = newIsActive ? (DateTimeOffset?)null : InactiveLockoutEnd;
                var lockoutEnabled = !newIsActive;

                // Direct EF update — Identity UserManager.UpdateAsync can throw or fail on
                // lockout-only changes for some legacy rows; projects/orgs use the same pattern.
                var rows = await _dbContext.ApplicationUsers
                    .Where(u => u.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.IsActive, newIsActive)
                        .SetProperty(u => u.LockoutEnabled, lockoutEnabled)
                        .SetProperty(u => u.LockoutEnd, lockoutEnd));

                if (rows == 0)
                    return new NotFoundObjectResult("User not found.");

                user.IsActive = newIsActive;
                user.LockoutEnabled = lockoutEnabled;
                user.LockoutEnd = lockoutEnd;

                _logger.LogInformation("Admin set user {Email} active={IsActive}.", user.Email, user.IsActive);
                return new OkObjectResult(ToDto(user, roles));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SetActiveUser failed for {UserId}.", id);
                return new BadRequestObjectResult("Could not update user status. Check application logs for details.");
            }
        }

        /// <summary>
        /// Admin: Archive a user (set inactive + hidden from all views except user management).
        /// </summary>
        [Function("ArchiveUser")]
        public async Task<IActionResult> ArchiveUserAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/{id}/archive")] HttpRequestData req, string id)
        {
            try
            {
                var principal = new ClaimsPrincipal(req.Identities);

                if (!Constants.Roles.IsGlobalAdmin(principal))
                    return new StatusCodeResult(403);

                if (string.IsNullOrWhiteSpace(id))
                    return new BadRequestObjectResult("User id is required.");

                var user = await _dbContext.ApplicationUsers.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == id);
                if (user == null)
                    return new NotFoundObjectResult("User not found.");

                var roles = await GetUserRolesAsync(id);
                if (!Constants.Roles.CanManageUser(principal, roles))
                    return new StatusCodeResult(403);

                if (user.Email?.Equals(principal.FindFirstValue(ClaimTypes.Email), StringComparison.OrdinalIgnoreCase) == true)
                    return new BadRequestObjectResult("You cannot archive your own account.");

                var rows = await _dbContext.ApplicationUsers
                    .Where(u => u.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.IsActive, false)
                        .SetProperty(u => u.LockoutEnabled, true)
                        .SetProperty(u => u.LockoutEnd, InactiveLockoutEnd)
                        .SetProperty(u => u.IsArchived, true));

                if (rows == 0)
                    return new NotFoundObjectResult("User not found.");

                ApplyInactiveLockout(user);
                user.IsArchived = true;

                _logger.LogInformation("Admin archived user {Email}.", user.Email);
                return new OkObjectResult(ToDto(user, roles));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ArchiveUser failed for {UserId}.", id);
                return new BadRequestObjectResult("Could not archive user. Check application logs for details.");
            }
        }

        /// <summary>
        /// Global Admin: Restore an archived user (clear archive flag and reactivate).
        /// Tracked tasks, submissions, and settings are never removed by archive — restore
        /// only reverses the account lockout flags so the user can sign in again.
        /// </summary>
        [Function("UnarchiveUser")]
        public async Task<IActionResult> UnarchiveUserAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/{id}/unarchive")] HttpRequestData req, string id)
        {
            try
            {
                var principal = new ClaimsPrincipal(req.Identities);

                if (!Constants.Roles.IsGlobalAdmin(principal))
                    return new StatusCodeResult(403);

                if (string.IsNullOrWhiteSpace(id))
                    return new BadRequestObjectResult("User id is required.");

                var user = await _dbContext.ApplicationUsers.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == id);
                if (user == null)
                    return new NotFoundObjectResult("User not found.");

                var roles = await GetUserRolesAsync(id);
                if (!Constants.Roles.CanManageUser(principal, roles))
                    return new StatusCodeResult(403);

                if (!user.IsArchived)
                    return new BadRequestObjectResult("User is not archived.");

                var rows = await _dbContext.ApplicationUsers
                    .Where(u => u.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.IsArchived, false)
                        .SetProperty(u => u.IsActive, true)
                        .SetProperty(u => u.LockoutEnabled, false)
                        .SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null));

                if (rows == 0)
                    return new NotFoundObjectResult("User not found.");

                user.IsArchived = false;
                user.IsActive = true;
                user.LockoutEnabled = false;
                user.LockoutEnd = null;

                _logger.LogInformation("Admin restored archived user {Email}.", user.Email);
                return new OkObjectResult(ToDto(user, roles));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UnarchiveUser failed for {UserId}.", id);
                return new BadRequestObjectResult("Could not restore user. Check application logs for details.");
            }
        }

        /// <summary>
        /// Global Admin: Force the target user to re-authenticate. Sets
        /// OidcSessionInvalidatedAt = now; AuthMiddleware will drop their identity until
        /// they sign in again (which bumps LastSignInAt back past this timestamp). Not a
        /// lockout — they can sign back in immediately. The Google grant is untouched.
        /// </summary>
        [Function("PurgeUserOidcToken")]
        public async Task<IActionResult> PurgeUserOidcTokenAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/{id}/purge-token")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (!Constants.Roles.IsGlobalAdmin(principal))
                return new StatusCodeResult(403);

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return new NotFoundObjectResult("User not found.");

            if (user.Email?.Equals(principal.FindFirstValue(ClaimTypes.Email), StringComparison.OrdinalIgnoreCase) == true)
                return new BadRequestObjectResult("You cannot purge your own session.");

            try
            {
                var invalidatedAt = DateTimeOffset.UtcNow;
                var rows = await _dbContext.ApplicationUsers
                    .Where(u => u.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.OidcSessionInvalidatedAt, invalidatedAt));

                if (rows == 0)
                    return new NotFoundObjectResult("User not found.");

                user.OidcSessionInvalidatedAt = invalidatedAt;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PurgeUserOidcToken failed for {UserId}.", id);
                return new BadRequestObjectResult("Could not purge user session. Check application logs for details.");
            }

            _logger.LogInformation("Admin purged OIDC token for user {Email}.", user.Email);

            var roles = await GetUserRolesAsync(id);
            return new OkObjectResult(ToDto(user, roles));
        }

        /// <summary>
        /// Global Admin: Revoke the target user's Google grant. Calls Google's /revoke
        /// on their refresh_token, clears all calendar linkage from UserSettings, clears
        /// stored GoogleEventIds on their tracked tasks, and invalidates their current
        /// OIDC session (the revoke kills their app token anyway, this just makes the
        /// app respect that immediately instead of waiting for tokeninfo cache expiry).
        /// </summary>
        [Function("PurgeUserOidcPermissions")]
        public async Task<IActionResult> PurgeUserOidcPermissionsAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/{id}/purge-permissions")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (!Constants.Roles.IsGlobalAdmin(principal))
                return new StatusCodeResult(403);

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return new NotFoundObjectResult("User not found.");

            if (user.Email?.Equals(principal.FindFirstValue(ClaimTypes.Email), StringComparison.OrdinalIgnoreCase) == true)
                return new BadRequestObjectResult("You cannot purge your own permissions.");

            // Revoke the Google grant (calendar refresh_token) if we hold one. This is
            // the same call we removed from user-initiated disconnect — admin-initiated
            // purge explicitly wants the revocation.
            var settings = await _dbContext.UserSettings.FirstOrDefaultAsync(s => s.UserId == user.Id);
            if (settings != null && !string.IsNullOrEmpty(settings.GoogleRefreshToken))
            {
                try
                {
                    await _googleCalendar.RevokeAsync(settings.GoogleRefreshToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Google revoke failed during admin purge for user {Email}; clearing local linkage anyway.", user.Email);
                }

                settings.GoogleRefreshToken = null;
                settings.GoogleCalendarId = null;
                settings.GoogleCalendarEmail = null;
                settings.GoogleChannelId = null;
                settings.GoogleChannelToken = null;
                settings.GoogleResourceId = null;
                settings.GoogleChannelExpiresAt = null;
                settings.GoogleSyncToken = null;
                _dbContext.UserSettings.Update(settings);
            }

            // Clear stored event IDs — the events are gone with the revoked grant.
            var taskRows = await _dbContext.TrackedTasks
                .Where(t => t.UserId == user.Id && t.GoogleEventId != null)
                .ToListAsync();
            foreach (var t in taskRows)
                t.GoogleEventId = null;

            // Force re-auth: revoking the calendar grant typically invalidates the SPA's
            // sign-in access_token too (shared OAuth grant). Setting this makes the app
            // recognize that immediately rather than serving requests until the
            // AuthMiddleware tokeninfo cache expires.
            user.OidcSessionInvalidatedAt = DateTimeOffset.UtcNow;

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "PurgeUserOidcPermissions database error for {UserId}.", user.Id);
                return new BadRequestObjectResult(
                    "Database error revoking Google permissions. Check application logs for details.");
            }

            var saveError = await CommitUserAsync(user, "purge user permissions");
            if (saveError != null)
                return saveError;

            _logger.LogInformation("Admin purged Google permissions for user {Email}.", user.Email);

            var roles = await _userManager.GetRolesAsync(user);
            return new OkObjectResult(ToDto(user, roles));
        }

        /// <summary>
        /// Admin: Delete a user and all their data.
        /// </summary>
        [Function("DeleteUser")]
        public async Task<IActionResult> DeleteUserAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "users/{id}")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);

            // Delete is global Admin only — scoped admins shouldn't be able to permanently
            // remove accounts even within their scope. Archive/deactivate stays available
            // to them.
            if (!Constants.Roles.IsGlobalAdmin(principal))
                return new StatusCodeResult(403);

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return new NotFoundObjectResult("User not found.");

            var roles = await _userManager.GetRolesAsync(user);
            if (!Constants.Roles.CanManageUser(principal, roles))
                return new StatusCodeResult(403);

            if (user.Email?.Equals(principal.FindFirstValue(ClaimTypes.Email), StringComparison.OrdinalIgnoreCase) == true)
                return new BadRequestObjectResult("You cannot delete your own account.");

            // Check app settings
            var allowDelete = await _dbContext.AppSettings.FindAsync(Constants.SettingKeys.AllowUserDelete);
            if (allowDelete == null || !bool.TryParse(allowDelete.Value, out var allowed) || !allowed)
                return new BadRequestObjectResult("User deletion is not enabled.");

            var retentionSetting = await _dbContext.AppSettings.FindAsync(Constants.SettingKeys.DataRetentionDays);
            var retentionDays = 2555; // default 7 years
            if (retentionSetting != null && int.TryParse(retentionSetting.Value, out var parsed))
                retentionDays = parsed;

            // Check if user has data newer than the retention period
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-retentionDays);
            var hasRecentData = await _dbContext.TrackedTasks
                .AnyAsync(t => t.UserId == id && t.StartDate > cutoffDate);

            if (hasRecentData)
                return new BadRequestObjectResult($"User has data newer than the {retentionDays}-day retention period. Cannot delete.");

            // Delete all user data
            var tasks = _dbContext.TrackedTasks.Where(t => t.UserId == id);
            _dbContext.TrackedTasks.RemoveRange(tasks);

            var settings = _dbContext.UserSettings.Where(s => s.UserId == id);
            _dbContext.UserSettings.RemoveRange(settings);

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "DeleteUser data cleanup database error for {UserId}.", user.Id);
                return new BadRequestObjectResult(
                    "Database error deleting user data. Check application logs for details.");
            }

            var deleteResult = await _userManager.DeleteAsync(user);
            if (!deleteResult.Succeeded)
            {
                var msg = string.Join(" ", deleteResult.Errors.Select(e => e.Description));
                _logger.LogWarning("DeleteUser identity delete failed for {UserId}: {Errors}", user.Id, msg);
                return new BadRequestObjectResult(string.IsNullOrWhiteSpace(msg)
                    ? "Could not delete user."
                    : msg);
            }

            _logger.LogInformation("Admin deleted user {Email} and all associated data.", user.Email);
            return new NoContentResult();
        }

        /// <summary>
        /// Admin: Get user data summary (whether they have data and the most recent date).
        /// </summary>
        [Function("GetUserDataInfo")]
        public async Task<IActionResult> GetUserDataInfoAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{id}/datainfo")] HttpRequestData req, string id)
        {
            var principal = new ClaimsPrincipal(req.Identities);
            if (!Constants.Roles.IsAnyAdmin(principal))
                return new StatusCodeResult(403);

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return new NotFoundObjectResult("User not found.");
            var roles = await _userManager.GetRolesAsync(user);
            if (!Constants.Roles.CanManageUser(principal, roles))
                return new StatusCodeResult(403);

            var taskCount = await _dbContext.TrackedTasks.CountAsync(t => t.UserId == id);
            var mostRecentDate = taskCount > 0
                ? await _dbContext.TrackedTasks.Where(t => t.UserId == id).MaxAsync(t => t.StartDate)
                : (DateTime?)null;

            return new OkObjectResult(new { taskCount, mostRecentDate });
        }

        /// <summary>
        /// Ensures scoped roles exist in the AspNetRoles table (e.g. "User:Tyme").
        /// ASP.NET Identity requires roles to exist before assigning them.
        /// </summary>
        private async Task EnsureRolesExist(IEnumerable<string> roles)
        {
            foreach (var role in roles)
            {
                if (!await _roleManager.RoleExistsAsync(role))
                {
                    await _roleManager.CreateAsync(new ApplicationRole
                    {
                        Name = role,
                        NormalizedName = role.ToUpperInvariant(),
                        Description = $"Auto-created scoped role: {role}"
                    });
                }
            }
        }

        /// <summary>
        /// Best-effort display name for a user we're about to create/heal. Tries in order:
        /// 1. The Google <c>name</c> claim on the principal (id_token path) — split into first/last.
        /// 2. Google's <c>/userinfo</c> endpoint called with the request's Bearer token —
        ///    returns real <c>given_name</c>/<c>family_name</c> even on the access_token path.
        /// 3. <see cref="UserNameRules.ParseFromEmail"/> on the email's local-part as final fallback.
        /// </summary>
        private async Task<(string FirstName, string LastName)> ResolveDisplayNameAsync(string? nameClaim, string email, HttpRequestData req)
        {
            if (!string.IsNullOrWhiteSpace(nameClaim) && !UserNameRules.LooksLikeEmail(nameClaim))
            {
                var parts = nameClaim.Trim().Split(' ', 2);
                if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                    return (parts[0], parts.Length > 1 ? parts[1] : string.Empty);
            }

            var fromGoogle = await TryFetchGoogleUserInfoAsync(req);
            if (fromGoogle is { GivenName: { Length: > 0 } given })
                return (given, fromGoogle.FamilyName ?? string.Empty);

            return UserNameRules.ParseFromEmail(email);
        }

        /// <summary>
        /// Forwards the request's Bearer token to Google's <c>/userinfo</c> endpoint. Returns
        /// null on any failure — callers fall back to the email-local-part heuristic.
        /// </summary>
        private async Task<GoogleUserInfo?> TryFetchGoogleUserInfoAsync(HttpRequestData req)
        {
            if (!req.Headers.TryGetValues("Authorization", out var authValues))
                return null;
            var authHeader = authValues.FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                var client = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
                request.Headers.TryAddWithoutValidation("Authorization", authHeader);
                using var response = await client.SendAsync(request, req.FunctionContext.CancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Google /userinfo returned {Status}; falling back to email-local heuristic.", (int)response.StatusCode);
                    return null;
                }
                return await response.Content.ReadFromJsonAsync<GoogleUserInfo>(cancellationToken: req.FunctionContext.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Google /userinfo call failed; falling back to email-local heuristic.");
                return null;
            }
        }

        private class GoogleUserInfo
        {
            [JsonPropertyName("given_name")]
            public string? GivenName { get; set; }

            [JsonPropertyName("family_name")]
            public string? FamilyName { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }
    }
}

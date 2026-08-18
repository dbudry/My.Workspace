using Microsoft.AspNetCore.Authorization;
using My.Shared.Constants;

namespace My.Client.Authorization;

public class ScopedRoleHandler : AuthorizationHandler<ScopedRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopedRoleRequirement requirement)
    {
        var hasAccess = requirement.ScopedOnly
            ? Constants.Roles.HasScopedAccess(context.User, requirement.Scope, requirement.MinimumRole)
            : Constants.Roles.HasAccess(context.User, requirement.Scope, requirement.MinimumRole);

        if (hasAccess)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

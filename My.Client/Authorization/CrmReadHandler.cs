using Microsoft.AspNetCore.Authorization;
using My.Shared.Constants;

namespace My.Client.Authorization;

public sealed class CrmReadHandler : AuthorizationHandler<CrmReadRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CrmReadRequirement requirement)
    {
        if (Constants.Roles.HasCrmRead(context.User))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

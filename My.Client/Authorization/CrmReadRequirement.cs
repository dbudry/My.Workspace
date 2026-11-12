using Microsoft.AspNetCore.Authorization;

namespace My.Client.Authorization;

/// <summary>CRM read: User:Crm and above, or exact Analyst:Crm.</summary>
public sealed class CrmReadRequirement : IAuthorizationRequirement
{
}

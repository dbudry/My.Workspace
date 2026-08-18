using My.Client.Services;

namespace My.Client.Handlers;

/// <summary>
/// Adds X-Impersonate-Role as a comma-separated list of roles when an admin has
/// chosen to view the app as a downgraded role-set. The server middleware
/// validates that the real principal has global Admin before honoring it, then
/// replaces the role-claim set so endpoint authorization actually rejects
/// requests outside the chosen scope — making the test realistic.
/// </summary>
public class ImpersonationDelegatingHandler : DelegatingHandler
{
    public const string HeaderName = "X-Impersonate-Role";

    private readonly ImpersonationService _impersonation;

    public ImpersonationDelegatingHandler(ImpersonationService impersonation)
    {
        _impersonation = impersonation;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _impersonation.InitAsync();
        if (_impersonation.IsActive)
        {
            request.Headers.Remove(HeaderName);
            request.Headers.Add(HeaderName, string.Join(",", _impersonation.Roles));
        }
        return await base.SendAsync(request, cancellationToken);
    }
}

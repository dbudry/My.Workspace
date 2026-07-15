using Microsoft.AspNetCore.Components.Authorization;

namespace My.Client.Services;

/// <summary>
/// Holds the inner (unwrapped) OIDC <see cref="AuthenticationStateProvider"/> so
/// services can bust RemoteAuthenticationService's principal cache without
/// resolving the impersonation decorator registered as <see cref="AuthenticationStateProvider"/>.
/// </summary>
public sealed class OidcAuthenticationStateProvider
{
    public OidcAuthenticationStateProvider(AuthenticationStateProvider inner) => Inner = inner;

    public AuthenticationStateProvider Inner { get; }
}
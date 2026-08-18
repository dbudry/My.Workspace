using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using My.Client.Services;

namespace My.Client.Handlers;

/// <summary>
/// Outermost handler in the API client chain. Catches the OIDC library's
/// AccessTokenNotAvailableException — thrown when the access token has expired
/// and silent renewal against Google failed (e.g., Google session ended,
/// third-party cookies blocked) — and flips SessionExpiryService so the
/// MainLayout banner appears. Re-throws so callers' own catches still fire and
/// the friendly snackbar from AddApiError can format the failure.
/// </summary>
public class TokenExpiryDelegatingHandler : DelegatingHandler
{
    private readonly SessionExpiryService _sessionExpiry;

    public TokenExpiryDelegatingHandler(SessionExpiryService sessionExpiry)
    {
        _sessionExpiry = sessionExpiry;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            // Any successful response means tokens are working again — clears the
            // sticky banner if a background silent renewal restored the session.
            // Reset is a no-op when the flag isn't set, so this is cheap.
            if (response.IsSuccessStatusCode)
            {
                _sessionExpiry.Reset();
            }
            return response;
        }
        catch (AccessTokenNotAvailableException)
        {
            _sessionExpiry.MarkExpired();
            throw;
        }
    }
}

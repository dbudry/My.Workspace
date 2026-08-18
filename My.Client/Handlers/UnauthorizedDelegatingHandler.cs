using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.JSInterop;
using MudBlazor;

namespace My.Client.Handlers;

/// <summary>
/// Handles auth/permission failures returned by the API:
///
/// <list type="bullet">
///   <item><b>401</b> from a request where the client has no authenticated session — the cached
///   Google access token expired, was revoked, or silent renewal failed. Surface a "session
///   expired" snackbar and bounce through interactive sign-in.</item>
///   <item><b>401</b> from a request where the client <i>is</i> authenticated — the gate
///   should really have returned 403, but defensively treat it as a permission failure:
///   snackbar, no redirect. Redirecting here would loop through Google (session is fine) right
///   back to the same endpoint and 401 again.</item>
///   <item><b>403</b> — proper permission failure. Snackbar, no redirect.</item>
/// </list>
/// </summary>
public class UnauthorizedDelegatingHandler : DelegatingHandler
{
    private readonly NavigationManager _navigation;
    private readonly IJSRuntime _js;
    private readonly ISnackbar _snackbar;
    private readonly AuthenticationStateProvider _authStateProvider;
    private static bool _redirecting;

    public UnauthorizedDelegatingHandler(
        NavigationManager navigation,
        IJSRuntime js,
        ISnackbar snackbar,
        AuthenticationStateProvider authStateProvider)
    {
        _navigation = navigation;
        _js = js;
        _snackbar = snackbar;
        _authStateProvider = authStateProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            _snackbar.Add("You don't have permission to perform that action.", Severity.Warning);
            return response;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized && !_redirecting)
        {
            // Distinguish "session truly expired" from "authenticated but lacks role". The
            // gate should have returned 403 for the latter, but if it didn't we still must
            // not redirect — Google would happily re-issue a token and we'd loop back to
            // the same 401.
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            if (authState.User.Identity?.IsAuthenticated == true)
            {
                _snackbar.Add("You don't have permission to perform that action.", Severity.Warning);
                return response;
            }

            _redirecting = true;
            _snackbar.Add("Your session expired. Redirecting to sign-in…", Severity.Warning);

            try { await _js.InvokeVoidAsync("localStorage.removeItem", "userLoggedOut"); }
            catch { /* JS might be unavailable during prerender */ }

            var options = new InteractiveRequestOptions
            {
                Interaction = InteractionType.SignIn,
                ReturnUrl = _navigation.Uri
            };
            _navigation.NavigateToLogin("authentication/login", options);
        }

        return response;
    }
}

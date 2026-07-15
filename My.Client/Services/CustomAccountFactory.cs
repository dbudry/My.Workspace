using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using My.Shared.Constants;
using My.Shared.Dtos.User;

namespace My.Client.Services;

public class CustomAccountFactory : AccountClaimsPrincipalFactory<RemoteUserAccount>
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly NavigationManager _navigation;
    private readonly SessionExpiryService _sessionExpiry;
    private readonly ILogger<CustomAccountFactory> _logger;

    // Coalesces concurrent /api/users/provision calls. The OIDC library asks for
    // the principal multiple times during login-callback (RemoteAuthenticatorView,
    // the cascading auth state, AuthorizeView), and previously every ask kicked
    // off its own HTTP roundtrip. On a cold-start backend that stacked into
    // 20-30s of "Completing login…" before the auto-navigate could fire. Now one
    // call serves all callers; cleared on logout/error so retries refetch.
    private Task<UserDto?>? _provisionTask;

    /// <summary>
    /// Extra attempts after the first for transient cold-start failures.
    /// Total tries = 1 + MaxProvisionRetries. Delays match <see cref="Handlers.RetryDelegatingHandler"/>.
    /// </summary>
    internal const int MaxProvisionRetries = 3;

    private static readonly int[] ProvisionRetryDelaySeconds = [2, 4, 8];

    /// <summary>
    /// Delay between provision retries. Tests replace this with a no-op so cold-start
    /// backoff doesn't burn wall-clock time in CI.
    /// </summary>
    internal static Func<int, CancellationToken, Task> DelayAsync { get; set; } =
        static (seconds, ct) => Task.Delay(TimeSpan.FromSeconds(seconds), ct);

    public CustomAccountFactory(
        IAccessTokenProviderAccessor accessor,
        IHttpClientFactory clientFactory,
        NavigationManager navigation,
        SessionExpiryService sessionExpiry,
        ILogger<CustomAccountFactory> logger) : base(accessor)
    {
        _clientFactory = clientFactory;
        _navigation = navigation;
        _sessionExpiry = sessionExpiry;
        _logger = logger;
    }

    public override async ValueTask<ClaimsPrincipal> CreateUserAsync(RemoteUserAccount account, RemoteAuthenticationUserOptions options)
    {
        var user = await base.CreateUserAsync(account, options);
        if (user.Identity?.IsAuthenticated != true)
        {
            // Anonymous (logout or pre-login) — drop any cached provision so the
            // next sign-in fetches fresh roles in case a different user signs in.
            _provisionTask = null;
            return user;
        }

        var identity = (ClaimsIdentity)user.Identity;

        var task = _provisionTask ??= ProvisionAsync();

        UserDto? userDto;
        try
        {
            userDto = await task;
        }
        catch (AccessTokenNotAvailableException ex)
        {
            // Cached identity was restored from storage but the access token is gone
            // and silent renewal failed. Don't pretend everything's fine — surface the
            // expired session so the MainLayout banner appears with a sign-in prompt.
            _logger.LogInformation(ex, "Provision skipped: no access token available — session has expired.");
            _provisionTask = null;
            _sessionExpiry.MarkExpired();
            return user;
        }
        catch (Exception ex)
        {
            // Retries inside ProvisionAsync already exhausted. Return Google identity
            // without app roles; Dashboard detects missing app_user_id and offers Retry.
            _logger.LogWarning(ex, "User provision call failed after retries. User will have no roles until retry or next login.");
            _provisionTask = null;
            return user;
        }

        if (userDto is null)
        {
            // Permanent failure (4xx) or exhausted transient retries returning null.
            // Clear cache so a later RefreshAfterSelfRoleChangeAsync / Retry can re-hit provision.
            _provisionTask = null;
            _logger.LogWarning("User provision returned no profile. User will have no app_user_id/roles until retry.");
            return user;
        }

        if (userDto.Roles != null)
            RoleClaimsHelper.ApplyProvisionRoles(identity, userDto.Roles);

        if (!string.IsNullOrEmpty(userDto.Id))
        {
            foreach (var claim in identity.FindAll(Constants.Claims.AppUserId).ToList())
                identity.RemoveClaim(claim);
            identity.AddClaim(new Claim(Constants.Claims.AppUserId, userDto.Id));
        }

        return user;
    }

    /// <summary>Clears the coalesced provision task so the next <see cref="CreateUserAsync"/>
    /// call re-fetches roles from <c>POST /api/users/provision</c>.</summary>
    public void InvalidateProvisionCache() => _provisionTask = null;

    /// <summary>
    /// True when the principal is Google-authenticated but never received
    /// <c>app_user_id</c> from provision — typically cold-start / SQL failure.
    /// Not the same as "has no module roles" (those users still have an app user id).
    /// </summary>
    public static bool IsMissingAppProfile(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && string.IsNullOrEmpty(user.FindFirst(Constants.Claims.AppUserId)?.Value);

    private async Task<UserDto?> ProvisionAsync()
    {
        _logger.LogInformation("Provisioning user with API...");
        var client = _clientFactory.CreateClient(Constants.API.ClientName);

        for (int attempt = 0; attempt <= MaxProvisionRetries; attempt++)
        {
            try
            {
                var response = await client.PostAsync(Constants.API.User.Provision, null);

                if (response.IsSuccessStatusCode)
                {
                    var dto = await response.Content.ReadFromJsonAsync<UserDto>();
                    _logger.LogInformation("User provisioned successfully with {RoleCount} role(s).", dto?.Roles?.Count ?? 0);
                    return dto;
                }

                var status = response.StatusCode;
                if (!IsTransientProvisionStatus(status) || attempt >= MaxProvisionRetries)
                {
                    _logger.LogWarning(
                        "User provision returned {StatusCode} (attempt {Attempt}/{Max}). Giving up.",
                        (int)status, attempt + 1, MaxProvisionRetries + 1);
                    return null;
                }

                var delay = ProvisionRetryDelaySeconds[attempt];
                _logger.LogInformation(
                    "User provision returned {StatusCode} (attempt {Attempt}/{Max}). Server may be starting up — retrying in {Delay}s.",
                    (int)status, attempt + 1, MaxProvisionRetries + 1, delay);
                await DelayAsync(delay, CancellationToken.None);
            }
            catch (AccessTokenNotAvailableException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxProvisionRetries && IsTransientProvisionException(ex))
            {
                var delay = ProvisionRetryDelaySeconds[attempt];
                _logger.LogInformation(
                    ex,
                    "User provision failed (attempt {Attempt}/{Max}): {Message}. Retrying in {Delay}s.",
                    attempt + 1, MaxProvisionRetries + 1, ex.Message, delay);
                await DelayAsync(delay, CancellationToken.None);
            }
        }

        return null;
    }

    /// <summary>
    /// Transient statuses worth re-POSTing provision for. Unlike general
    /// <c>RetryPolicy</c> (which skips POST 500 to protect OAuth code burn),
    /// provision is safe to retry: existing users only stamp LastSignInAt;
    /// first-user bootstrap is guarded by "any users exist" checks.
    ///
    /// Only 500/408 are handled here. 502/503/504 are already retried by
    /// <c>RetryDelegatingHandler</c> for all methods; re-wrapping them would
    /// stack multi-second delays on cold start (login spinner for a minute+).
    /// </summary>
    internal static bool IsTransientProvisionStatus(HttpStatusCode status) =>
        status is HttpStatusCode.InternalServerError
            or HttpStatusCode.RequestTimeout;

    internal static bool IsTransientProvisionException(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or IOException;
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using My.Functions.Helpers;
using Microsoft.IdentityModel.Tokens;
using My.DAL.Models;
using My.Shared.Constants;
using My.Shared.Rules;

namespace My.Functions
{
    public class AuthMiddleware : IFunctionsWorkerMiddleware
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _googleClientId;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AuthMiddleware> _logger;

        // Google's OIDC discovery — fetches and caches signing keys automatically
        private static readonly ConfigurationManager<OpenIdConnectConfiguration> _oidcConfigManager =
            new("https://accounts.google.com/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());

        public AuthMiddleware(
            IHttpClientFactory httpClientFactory,
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            IMemoryCache cache,
            ILogger<AuthMiddleware> logger)
        {
            _httpClientFactory = httpClientFactory;
            // Optional at host startup so self-hosters can boot the API and complete the setup wizard.
            // Requests still fail auth until a real ClientId is configured.
            _googleClientId = configuration["Google:ClientId"]
                ?? Environment.GetEnvironmentVariable("Google__ClientId")
                ?? string.Empty;
            _cache = cache;
            _logger = logger;
        }

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            var req = await context.GetHttpRequestDataAsync();
            if (req == null)
            {
                await next(context);
                return;
            }

            string? googleSub = null;
            string? email = null;
            string? name = null;

            // Validate the Google Bearer token cryptographically. This is the single source of
            // trust — x-ms-client-principal is not read, since it can be spoofed on a publicly
            // reachable Function App.
            var hadAuthHeader = false;
            if (req.Headers.TryGetValues("Authorization", out var authValues))
            {
                var authHeader = authValues.FirstOrDefault();
                if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
                {
                    hadAuthHeader = true;
                    var token = authHeader.Substring("Bearer ".Length).Trim();

                    // Try local JWT validation first (id_token), then fall back to tokeninfo with cache (access_token)
                    var tokenInfo = string.IsNullOrWhiteSpace(_googleClientId)
                        ? null
                        : await ValidateAsJwtAsync(token) ?? await ValidateAccessTokenCachedAsync(token);

                    if (tokenInfo != null)
                    {
                        var db = context.InstanceServices.GetRequiredService<My.DAL.Data.ApplicationDbContext>();
                        var allowedDomains = await AuthDomainSettingsLoader.ResolveAsync(db, _cache);
                        if (!GoogleIdentityRules.IsAllowedGoogleIdentity(tokenInfo.Email, tokenInfo.EmailVerified, allowedDomains))
                        {
                            _logger.LogWarning(
                                "Google token rejected for {Method} {Path}: email domain not allowed or not verified (got {Email}, verified={Verified}, policy={Policy}).",
                                req.Method, req.Url.AbsolutePath, tokenInfo.Email, tokenInfo.EmailVerified,
                                string.IsNullOrWhiteSpace(allowedDomains) ? "(not configured)" : allowedDomains);
                            tokenInfo = null;
                        }
                    }

                    if (tokenInfo != null)
                    {
                        googleSub = tokenInfo.Sub;
                        email = tokenInfo.Email;
                        // Prefer the real display name from the id_token's "name" claim;
                        // fall back to email-local-part for the access_token / tokeninfo
                        // path, which doesn't return a name. UserFunction.ProvisionAsync's
                        // heal logic only refreshes FirstName when the existing value looks
                        // auto-generated, so passing email here doesn't corrupt a name a
                        // later id_token request will overwrite correctly.
                        name = tokenInfo.Name ?? tokenInfo.Email;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Bearer token validation failed for {Method} {Path}. Both JWT and tokeninfo validation returned null. See preceding diagnostic logs for the specific reason.",
                            req.Method, req.Url.AbsolutePath);
                    }
                }
            }

            if (!hadAuthHeader)
            {
                _logger.LogDebug("No Bearer token on request to {Method} {Path}.", req.Method, req.Url.AbsolutePath);
            }

            // If we have a valid Google identity, look up user and roles in DB
            if (!string.IsNullOrEmpty(googleSub) && !string.IsNullOrEmpty(email))
            {
                var claimsId = new ClaimsIdentity("GoogleOIDC");
                claimsId.AddClaim(new Claim(ClaimTypes.Name, name ?? string.Empty));
                claimsId.AddClaim(new Claim(ClaimTypes.Email, email));

                // Look up user roles from the database
                var userManager = context.InstanceServices.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.FindByEmailAsync(email);
                if (user != null)
                {
                    // Honor admin-initiated OIDC session invalidation. If an admin used
                    // "Force re-sign-in" or "Revoke Google permissions" on this user,
                    // OidcSessionInvalidatedAt is set; we accept the request only if a
                    // sign-in has happened since (LastSignInAt > the invalidation). The
                    // provision endpoint is exempt — it's the endpoint that bumps
                    // LastSignInAt back to current, so blocking it would create a
                    // permanent lockout rather than the re-auth nudge we want.
                    var isProvisioning = req.Url.AbsolutePath.EndsWith("/users/provision", StringComparison.OrdinalIgnoreCase);
                    if (!isProvisioning && user.OidcSessionInvalidatedAt.HasValue
                        && (user.LastSignInAt is null || user.LastSignInAt < user.OidcSessionInvalidatedAt))
                    {
                        _logger.LogInformation("OIDC session invalidated for user {UserId}; dropping identity for {Method} {Path}.",
                            user.Id, req.Method, req.Url.AbsolutePath);
                        await next(context);
                        return;
                    }

                    // Use the database user ID (not Google sub) so FKs on TrackedTask/Project work
                    claimsId.AddClaim(new Claim(Constants.Claims.UserId, user.Id));

                    // Throttle LastLoginDate writes. Updating on every API call forces a SQL
                    // write on the cold path (token validated → user lookup → write → roles),
                    // which amplifies timeouts after idle. 15 minutes is fine for "last seen"
                    // reporting and keeps the first authenticated request cheaper under load.
                    var loginStampStale = user.LastLoginDate is null
                        || user.LastLoginDate < DateTimeOffset.UtcNow.AddMinutes(-15);
                    if (loginStampStale)
                    {
                        user.LastLoginDate = DateTimeOffset.UtcNow;
                        IdentityUserHealing.EnsureStamps(user);
                        await userManager.UpdateAsync(user);
                    }

                    // Cache the role list per user for ~60s. Roles change rarely; a stale
                    // window is acceptable and saves a DB round-trip on *every* API call.
                    var rolesCacheKey = $"userroles:{user.Id}";
                    if (!_cache.TryGetValue<IList<string>>(rolesCacheKey, out var roles) || roles is null)
                    {
                        roles = await userManager.GetRolesAsync(user);
                        _cache.Set(rolesCacheKey, roles, TimeSpan.FromSeconds(60));
                    }
                    foreach (var role in roles)
                    {
                        claimsId.AddClaim(new Claim(ClaimTypes.Role, role));
                    }

                    // Add fullname claim
                    claimsId.AddClaim(new Claim(Constants.Claims.Fullname, $"{user.FirstName} {user.LastName}"));
                }
                else
                {
                    // User not in DB yet — use Google sub as fallback (provision endpoint will handle creation)
                    claimsId.AddClaim(new Claim(Constants.Claims.UserId, googleSub));
                }

                // Apply impersonation: if a real Admin sends X-Impersonate-Role, strip the higher
                // role claims so endpoint authorization treats them as the lower role for testing.
                ApplyImpersonation(claimsId, req);

                ((ICollection<ClaimsIdentity>)req.Identities).Add(claimsId);
            }

            await next(context);
        }

        private static void ApplyImpersonation(ClaimsIdentity identity, Microsoft.Azure.Functions.Worker.Http.HttpRequestData req)
        {
            if (!req.Headers.TryGetValues("X-Impersonate-Role", out var values)) return;

            // Only the *global* Admin role can impersonate — scoped admins (e.g. Admin:Tyme)
            // cannot. Check this against the real claim set before we modify anything.
            var hasGlobalAdmin = identity.FindAll(ClaimTypes.Role).Any(c => c.Value == Constants.Roles.Admin);
            if (!hasGlobalAdmin) return;

            var raw = string.Join(",", values);
            var requested = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(IsValidRoleShape)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // Replace the role-claim set with the impersonated list. Empty list is a valid
            // test case ("no roles") — every protected endpoint will then refuse the request.
            foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList())
                identity.RemoveClaim(claim);
            foreach (var role in requested)
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        private static bool IsValidRoleShape(string value)
        {
            // Accepted forms: "Admin", "Manager", "User", or "<base>:<scope>" where base is one
            // of those three and scope is alphanumeric/underscore. Defends against the header
            // being used to inject arbitrary role strings even though the caller is a real Admin.
            var parts = value.Split(':');
            if (parts.Length is 0 or > 2) return false;
            var baseRole = parts[0];
            if (baseRole != Constants.Roles.Admin
                && baseRole != Constants.Roles.Manager
                && baseRole != Constants.Roles.User) return false;
            if (parts.Length == 1) return true;
            var scope = parts[1];
            if (string.IsNullOrEmpty(scope)) return false;
            foreach (var ch in scope)
                if (!char.IsLetterOrDigit(ch) && ch != '_') return false;
            return true;
        }

        /// <summary>
        /// Validates the token as a Google JWT (id_token) using Google's public signing keys.
        /// No external HTTP call per request — keys are cached by the ConfigurationManager.
        /// </summary>
        private async Task<GoogleTokenInfo?> ValidateAsJwtAsync(string token)
        {
            try
            {
                var oidcConfig = await _oidcConfigManager.GetConfigurationAsync(CancellationToken.None);

                var validationParams = new TokenValidationParameters
                {
                    ValidIssuer = "https://accounts.google.com",
                    ValidAudience = _googleClientId,
                    IssuerSigningKeys = oidcConfig.SigningKeys,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                };

                var handler = new JwtSecurityTokenHandler();
                var principal = handler.ValidateToken(token, validationParams, out _);

                var sub = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
                var email = principal.FindFirstValue("email") ?? principal.FindFirstValue(ClaimTypes.Email);
                // Google's id_token carries the user's display name in the "name" claim.
                // Preserve it here so the middleware can forward it onto ClaimTypes.Name
                // for downstream heal logic — previously this was dropped and the email
                // was used as a stand-in, which silently broke name resolution.
                var name = principal.FindFirstValue("name") ?? principal.FindFirstValue(ClaimTypes.Name);
                var emailVerified = principal.FindFirstValue("email_verified");

                if (!string.IsNullOrEmpty(sub) && !string.IsNullOrEmpty(email))
                    return new GoogleTokenInfo { Sub = sub, Email = email, EmailVerified = emailVerified, Name = name };
            }
            catch (SecurityTokenExpiredException)
            {
                // Genuinely expired id_token — caller will fall through to tokeninfo for the access_token path.
                _logger.LogDebug("JWT validation: id_token expired (expected when client sent access_token instead).");
            }
            catch (Exception ex) when (ex is ArgumentException or SecurityTokenMalformedException or SecurityTokenInvalidSignatureException)
            {
                // Token isn't a JWT at all (opaque access_token) or signature failed.
                _logger.LogDebug("JWT validation skipped: token is not a JWT ({ExceptionType}).", ex.GetType().Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JWT validation threw an unexpected exception. Falling through to tokeninfo.");
            }
            return null;
        }

        /// <summary>
        /// Validates an opaque Google access_token via Google's tokeninfo endpoint,
        /// with results cached to avoid repeated HTTP calls.
        /// </summary>
        private async Task<GoogleTokenInfo?> ValidateAccessTokenCachedAsync(string accessToken)
        {
            var cacheKey = $"gtoken:{ComputeTokenHash(accessToken)}";

            if (_cache.TryGetValue(cacheKey, out GoogleTokenInfo? cached))
                return cached;

            var tokenInfo = await ValidateAccessTokenAsync(accessToken);
            if (tokenInfo != null)
            {
                _cache.Set(cacheKey, tokenInfo, TimeSpan.FromMinutes(5));
            }

            return tokenInfo;
        }

        private async Task<GoogleTokenInfo?> ValidateAccessTokenAsync(string accessToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.GetAsync(
                    $"https://oauth2.googleapis.com/tokeninfo?access_token={accessToken}");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    // Google tokeninfo returns email_verified as a JSON boolean; JWT claims
                    // use strings. Deserialize with JsonElement so both shapes work — a
                    // string-typed property throws and dropped the whole identity (403/401
                    // on provision for every SPA access_token request).
                    GoogleTokenInfo? tokenInfo;
                    try
                    {
                        tokenInfo = JsonSerializer.Deserialize<GoogleTokenInfo>(json);
                        if (tokenInfo != null)
                            tokenInfo.EmailVerified = ReadEmailVerified(tokenInfo.EmailVerifiedRaw);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "tokeninfo JSON deserialize failed. Body length={Length}.", json.Length);
                        return null;
                    }

                    if (tokenInfo?.Sub != null && tokenInfo.Aud == _googleClientId)
                        return tokenInfo;

                    if (tokenInfo?.Sub == null)
                    {
                        _logger.LogWarning("tokeninfo returned 200 but the JSON had no 'sub' claim. Body length={Length}.", json.Length);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "tokeninfo audience mismatch: token aud={TokenAud} but configured Google:ClientId ends with …{ConfiguredSuffix}. The function app config does not match the OAuth client used by the SPA.",
                            tokenInfo.Aud,
                            _googleClientId.Length > 10 ? _googleClientId.Substring(_googleClientId.Length - 10) : _googleClientId);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "tokeninfo returned {StatusCode} ({StatusName}).",
                        (int)response.StatusCode, response.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "tokeninfo HTTP call failed (network/DNS/TLS). Inner: {Inner}", ex.InnerException?.Message);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "tokeninfo HTTP call timed out.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "tokeninfo validation threw an unexpected exception ({ExceptionType}).", ex.GetType().Name);
            }
            return null;
        }

        private static string ComputeTokenHash(string token)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToBase64String(hash);
        }

        private static string? ReadEmailVerified(JsonElement raw)
        {
            return raw.ValueKind switch
            {
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.String => GoogleIdentityRules.CoerceEmailVerified(raw.GetString()),
                JsonValueKind.Number => raw.GetInt32() != 0 ? "true" : "false",
                JsonValueKind.Undefined => null,
                JsonValueKind.Null => null,
                _ => GoogleIdentityRules.CoerceEmailVerified(raw.ToString())
            };
        }

        private class GoogleTokenInfo
        {
            [JsonPropertyName("sub")]
            public string? Sub { get; set; }

            [JsonPropertyName("email")]
            public string? Email { get; set; }

            /// <summary>
            /// Raw Google claim — bool from tokeninfo, string from some providers.
            /// Normalized into <see cref="EmailVerified"/> after deserialize.
            /// </summary>
            [JsonPropertyName("email_verified")]
            public JsonElement EmailVerifiedRaw { get; set; }

            /// <summary>Normalized "true"/"false" for <see cref="GoogleIdentityRules"/>.</summary>
            [JsonIgnore]
            public string? EmailVerified { get; set; }

            [JsonPropertyName("aud")]
            public string? Aud { get; set; }

            /// <summary>
            /// Full display name from the Google id_token's <c>name</c> claim. Null on
            /// the tokeninfo (access_token) path — that endpoint doesn't return it — but
            /// populated on the JWT (id_token) path. <see cref="AuthMiddleware"/> forwards
            /// whatever it gets here onto <c>ClaimTypes.Name</c> so downstream code (like
            /// <c>UserFunction.ProvisionAsync</c>'s heal logic) can use the real name
            /// instead of the email.
            /// </summary>
            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }
    }
}

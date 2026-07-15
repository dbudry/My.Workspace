using Blazored.SessionStorage;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Microsoft.AspNetCore.Authorization;
using My.Client;
using My.Client.Authorization;
using My.Client.Services;
using My.Client.Handlers;
using My.Shared.Constants;


var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrlConfig = builder.Configuration["ApiBaseUrl"]
    ?? throw new InvalidOperationException("ApiBaseUrl is not configured in appsettings.json");

// Resolve relative URLs against the host base address.
// Production uses "api/" which resolves to same-origin through SWA proxy (no CORS).
// Development uses the full Azure Functions URL (CORS handled by middleware).
var apiBaseUrl = new Uri(new Uri(builder.HostEnvironment.BaseAddress), apiBaseUrlConfig).ToString();

// Google OIDC authentication (direct from browser — no server needed)
builder.Services.AddOidcAuthentication(options =>
{
    options.ProviderOptions.Authority = "https://accounts.google.com";
    options.ProviderOptions.ClientId = builder.Configuration["Authentication:Google:ClientId"];
    options.ProviderOptions.ResponseType = "id_token token";
    options.ProviderOptions.DefaultScopes.Add("openid");
    options.ProviderOptions.DefaultScopes.Add("profile");
    options.ProviderOptions.DefaultScopes.Add("email");
    // Optional Google hosted-domain hint when exactly one email domain is configured
    // (server still enforces Auth__AllowedEmailDomains / setup policy).
    var domainPolicy = builder.Configuration["Auth:AllowedEmailDomains"]
        ?? builder.Configuration["Auth__AllowedEmailDomains"];
    var hdHint = My.Shared.Rules.GoogleIdentityRules.GetSingleHostedDomainHint(domainPolicy);
    if (!string.IsNullOrEmpty(hdHint))
        options.ProviderOptions.AdditionalProviderParameters.Add("hd", hdHint);
}).AddAccountClaimsPrincipalFactory<CustomAccountFactory>();

// Configure named HttpClient that attaches the Bearer token to API calls
builder.Services.AddTransient<TokenExpiryDelegatingHandler>();
builder.Services.AddTransient<RetryDelegatingHandler>();
builder.Services.AddTransient<UnauthorizedDelegatingHandler>();
builder.Services.AddTransient<ImpersonationDelegatingHandler>();
builder.Services.AddHttpClient(Constants.API.ClientName, client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.DefaultRequestHeaders.Add("accept", "application/json");
})
// TokenExpiryDelegatingHandler is outermost so it sees AccessTokenNotAvailableException
// thrown by AuthorizationMessageHandler before any other handler catches it.
.AddHttpMessageHandler<TokenExpiryDelegatingHandler>()
.AddHttpMessageHandler<RetryDelegatingHandler>()
.AddHttpMessageHandler<UnauthorizedDelegatingHandler>()
.AddHttpMessageHandler<ImpersonationDelegatingHandler>()
.AddHttpMessageHandler(sp =>
{
    var handler = new AuthorizationMessageHandler(
        sp.GetRequiredService<IAccessTokenProvider>(),
        sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>());
    handler.ConfigureHandler(authorizedUrls: new[] { apiBaseUrl });
    return handler;
});

builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

builder.Services.AddBlazoredSessionStorage();
builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<UserSettingsService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<NavigationPersistenceService>();
builder.Services.AddScoped<ImpersonationService>();
builder.Services.AddScoped<SessionExpiryService>();
builder.Services.AddScoped<TimeSubmissionEvents>();
builder.Services.AddScoped<IntranetNavigationEvents>();
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});
builder.Services.AddScoped<MaterialIconsCatalog>();
builder.Services.AddScoped<ProjectsCache>();
builder.Services.AddScoped<TrackedTasksClient>();
builder.Services.AddScoped<StopwatchItemsClient>();
builder.Services.AddScoped<StopwatchLocalCache>();
builder.Services.AddScoped<OrganizationsCache>();
builder.Services.AddScoped<AppSettingsCache>();
builder.Services.AddScoped<IntranetMediaService>();
builder.Services.AddScoped<IntranetMediaPolicyService>();
builder.Services.AddScoped<HeaderSearchUiState>();
builder.Services.AddScoped<IntranetSearchService>();
builder.Services.AddMudServices();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, ScopedRolePolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, ScopedRoleHandler>();

// Decorate the OIDC AuthenticationStateProvider so impersonation can downgrade
// the visible role on the client. Server-side enforcement is in AuthMiddleware.
//
// The OIDC library registers RemoteAuthenticationService<,,> as the implementation
// of AuthenticationStateProvider only — it isn't separately registered under its own
// type, and the IRemoteAuthenticationService<> / IAccessTokenProvider services are
// factories that cast from AuthenticationStateProvider. So when we wrap, we have to:
//   1. Re-register the concrete OIDC service under its own type so the wrapper can
//      hold a real reference and login/logout still works.
//   2. Re-point IRemoteAuthenticationService<> and IAccessTokenProvider at the
//      concrete type (otherwise the casts would target our wrapper and fail).
var oidcAuthDescriptor = builder.Services.Single(s => s.ServiceType == typeof(AuthenticationStateProvider));
var oidcImplType = oidcAuthDescriptor.ImplementationType
    ?? throw new InvalidOperationException("OIDC AuthenticationStateProvider has no ImplementationType — DI shape changed.");

builder.Services.Remove(oidcAuthDescriptor);
builder.Services.AddScoped(oidcImplType);

var stateType = oidcImplType.GetGenericArguments()[0]; // TRemoteAuthenticationState
var iRemoteAuthServiceType = typeof(IRemoteAuthenticationService<>).MakeGenericType(stateType);
ReRouteToConcrete(builder.Services, iRemoteAuthServiceType, oidcImplType);
ReRouteToConcrete(builder.Services, typeof(IAccessTokenProvider), oidcImplType);

builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
{
    var inner = (AuthenticationStateProvider)sp.GetRequiredService(oidcImplType);
    var imp = sp.GetRequiredService<ImpersonationService>();
    return new ImpersonationAuthStateProvider(inner, imp);
});
builder.Services.AddScoped(sp =>
    (ImpersonationAuthStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());

builder.Services.AddRoleRefreshServices(oidcImplType);

static void ReRouteToConcrete(IServiceCollection services, Type serviceType, Type concreteType)
{
    var existing = services.SingleOrDefault(s => s.ServiceType == serviceType);
    if (existing != null) services.Remove(existing);
    services.AddScoped(serviceType, sp => sp.GetRequiredService(concreteType));
}

await builder.Build().RunAsync();

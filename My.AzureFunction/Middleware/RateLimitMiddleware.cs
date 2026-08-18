using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using My.DAL.Data;
using My.Functions.Helpers;
using My.Shared.Constants;
using My.Shared.Rules;

namespace My.Functions;

public class RateLimitMiddleware : IFunctionsWorkerMiddleware
{
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RateLimitMiddleware> _logger;

    public RateLimitMiddleware(
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<RateLimitMiddleware> logger)
    {
        _cache = cache;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext == null)
        {
            await next(context);
            return;
        }

        var configEnabled = _configuration["RateLimit:Enabled"];
        RateLimitSettings settings;
        try
        {
            var dbContext = context.InstanceServices.GetRequiredService<ApplicationDbContext>();
            settings = await RateLimitSettingsLoader.LoadAsync(dbContext, _cache, configEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rate limit settings load failed; allowing request through.");
            await next(context);
            return;
        }

        if (!settings.Enabled)
        {
            await next(context);
            return;
        }

        var request = httpContext.Request;
        var normalizedPath = RateLimitRules.NormalizeApiPath(request.Path.Value);
        var hadBearerHeader = request.Headers.Authorization
            .FirstOrDefault()
            ?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true;

        var userId = httpContext.User.FindFirstValue(Constants.Claims.UserId);
        var clientIp = GetClientIp(httpContext);

        var requestContext = new RateLimitRequestContext(
            normalizedPath,
            request.Method,
            string.IsNullOrEmpty(userId) ? null : userId,
            clientIp,
            hadBearerHeader);

        var options = settings.ToOptions();
        var policy = RateLimitRules.Resolve(requestContext, options);
        if (policy.IsExempt)
        {
            await next(context);
            return;
        }

        var utcNow = DateTimeOffset.UtcNow;
        if (RateLimitCounter.TryAcquire(_cache, policy, utcNow, out var retryAfterSeconds))
        {
            await next(context);
            return;
        }

        _logger.LogInformation(
            "Rate limit exceeded for {Method} {Path} bucket={BucketKey} ip={ClientIp} user={UserId}",
            request.Method,
            request.Path.Value,
            policy.BucketKey,
            clientIp,
            userId ?? "(anonymous)");

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        httpContext.Response.ContentType = "application/json; charset=utf-8";

        var body = JsonSerializer.Serialize(new { message = RateLimitRules.TooManyRequestsMessage });
        await httpContext.Response.WriteAsync(body, Encoding.UTF8);
    }

    private static string GetClientIp(HttpContext httpContext)
    {
        var forwarded = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
                return first;
        }

        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
using ConstantsClass = My.Shared.Constants.Constants;

namespace My.Shared.Rules;

/// <summary>
/// Per-route API rate limits for the Functions host. Generous defaults for normal SPA use;
/// tighter caps on expensive or anonymous endpoints. Toggle via App Settings or RateLimit__Enabled.
/// </summary>
public static class RateLimitRules
{
    public const int DefaultAuthenticatedPerMinute = 300;
    public const int DefaultAnonymousPerMinute = 60;
    public const int InvalidBearerPerMinute = 30;
    public const int ProvisionPerMinute = 5;
    public const int FetchExternalImagePerMinute = 10;
    public const int UploadPerMinute = 20;
    public const int HeavyReadPerMinute = 10;

    public const string TooManyRequestsMessage =
        "Too many requests. Please wait a moment and try again.";

    public static RateLimitOptions BuildOptions(bool enabled) =>
        BuildOptions(
            enabled,
            DefaultAuthenticatedPerMinute,
            DefaultAnonymousPerMinute,
            InvalidBearerPerMinute,
            ProvisionPerMinute,
            FetchExternalImagePerMinute,
            UploadPerMinute,
            HeavyReadPerMinute);

    public static RateLimitOptions BuildOptions(
        bool enabled,
        int authenticatedPerMinute,
        int anonymousPerMinute,
        int invalidBearerPerMinute,
        int provisionPerMinute,
        int fetchExternalImagePerMinute,
        int uploadPerMinute,
        int heavyReadPerMinute)
    {
        return new RateLimitOptions
        {
            Enabled = enabled,
            DefaultAuthenticatedPerMinute = RateLimitSettings.ClampPermits(authenticatedPerMinute),
            DefaultAnonymousPerMinute = RateLimitSettings.ClampPermits(anonymousPerMinute),
            InvalidBearerPerMinute = RateLimitSettings.ClampPermits(invalidBearerPerMinute),
            ProvisionPerMinute = RateLimitSettings.ClampPermits(provisionPerMinute),
            FetchExternalImagePerMinute = RateLimitSettings.ClampPermits(fetchExternalImagePerMinute),
            UploadPerMinute = RateLimitSettings.ClampPermits(uploadPerMinute),
            HeavyReadPerMinute = RateLimitSettings.ClampPermits(heavyReadPerMinute)
        };
    }

    public static int ComputeRetryAfterSeconds(DateTimeOffset utcNow)
    {
        var secondsIntoMinute = (int)(utcNow.ToUnixTimeSeconds() % 60);
        return Math.Max(1, 60 - secondsIntoMinute);
    }

    public static string NormalizeApiPath(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            return string.Empty;

        var path = absolutePath.Trim().TrimEnd('/');
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            path = path[5..];
        else if (path.Equals("/api", StringComparison.OrdinalIgnoreCase))
            path = string.Empty;

        return path.TrimStart('/').ToLowerInvariant();
    }

    public static bool IsExempt(string normalizedPath, string httpMethod)
    {
        if (httpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalizedPath.Equals(ConstantsClass.API.GoogleCalendar.Webhook, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.EndsWith("/" + ConstantsClass.API.GoogleCalendar.Webhook, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static RateLimitPolicy Resolve(RateLimitRequestContext context, RateLimitOptions options)
    {
        if (!options.Enabled || IsExempt(context.NormalizedPath, context.HttpMethod))
            return RateLimitPolicy.Exempt;

        if (context.HadBearerHeader
            && string.IsNullOrEmpty(context.UserId)
            && !IsProvisionPath(context.NormalizedPath))
        {
            return new RateLimitPolicy(
                $"auth-invalid:{context.ClientIp}",
                options.InvalidBearerPerMinute);
        }

        if (IsProvisionPath(context.NormalizedPath))
        {
            return new RateLimitPolicy(
                $"provision:{context.ClientIp}",
                options.ProvisionPerMinute);
        }

        var strict = ResolveStrictAuthenticatedPolicy(context, options);
        if (strict != null)
            return strict;

        if (!string.IsNullOrEmpty(context.UserId))
        {
            return new RateLimitPolicy(
                $"user:{context.UserId}",
                options.DefaultAuthenticatedPerMinute);
        }

        return new RateLimitPolicy(
            $"ip:{context.ClientIp}",
            options.DefaultAnonymousPerMinute);
    }

    private static RateLimitPolicy? ResolveStrictAuthenticatedPolicy(
        RateLimitRequestContext context,
        RateLimitOptions options)
    {
        if (string.IsNullOrEmpty(context.UserId))
            return null;

        var method = context.HttpMethod;
        var path = context.NormalizedPath;

        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && path.Contains("fetch-external-image", StringComparison.Ordinal))
        {
            return new RateLimitPolicy($"user:{context.UserId}:fetch-image", options.FetchExternalImagePerMinute);
        }

        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && path.Contains("/documents/upload", StringComparison.Ordinal))
        {
            return new RateLimitPolicy($"user:{context.UserId}:upload", options.UploadPerMinute);
        }

        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && (path.Equals(ConstantsClass.API.Logs.Get, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(ConstantsClass.API.Logs.Get + "/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/dataextraction", StringComparison.Ordinal)))
        {
            return new RateLimitPolicy($"user:{context.UserId}:heavy-read", options.HeavyReadPerMinute);
        }

        return null;
    }

    private static bool IsProvisionPath(string normalizedPath) =>
        normalizedPath.Equals(ConstantsClass.API.User.Provision, StringComparison.OrdinalIgnoreCase)
        || normalizedPath.EndsWith("/" + ConstantsClass.API.User.Provision, StringComparison.OrdinalIgnoreCase);
}

public sealed record RateLimitRequestContext(
    string NormalizedPath,
    string HttpMethod,
    string? UserId,
    string ClientIp,
    bool HadBearerHeader);

public sealed record RateLimitPolicy(string BucketKey, int PermitsPerMinute)
{
    public static RateLimitPolicy Exempt { get; } = new(string.Empty, 0);

    public bool IsExempt => PermitsPerMinute <= 0 && string.IsNullOrEmpty(BucketKey);
}

public sealed class RateLimitOptions
{
    public bool Enabled { get; init; }
    public int DefaultAuthenticatedPerMinute { get; init; }
    public int DefaultAnonymousPerMinute { get; init; }
    public int InvalidBearerPerMinute { get; init; }
    public int ProvisionPerMinute { get; init; }
    public int FetchExternalImagePerMinute { get; init; }
    public int UploadPerMinute { get; init; }
    public int HeavyReadPerMinute { get; init; }
}
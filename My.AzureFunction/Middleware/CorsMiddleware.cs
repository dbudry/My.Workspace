using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace My.Functions
{
    public class CorsMiddleware : IFunctionsWorkerMiddleware
    {
        private static readonly HashSet<string> AllowedOrigins = new(StringComparer.OrdinalIgnoreCase)
        {
            "https://localhost:7047"
        };

        /// <summary>Impersonation header sent by <c>ImpersonationDelegatingHandler</c> on API calls.</summary>
        private const string ImpersonateRoleHeader = "X-Impersonate-Role";

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            var httpContext = context.GetHttpContext();
            if (httpContext == null)
            {
                await next(context);
                return;
            }

            var origin = httpContext.Request.Headers["Origin"].FirstOrDefault();
            var isAllowedOrigin = IsAllowedOrigin(origin);

            // Short-circuit preflight (OPTIONS) immediately for allowed origins.
            // This ensures ACAO (and other CORS headers) are present on the preflight response
            // itself. Without a matching function (see PreflightFunction.cs catch-all), the
            // worker middleware is never invoked for OPTIONS and the browser blocks the
            // subsequent real request with a CORS error.
            if (isAllowedOrigin && httpContext.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                SetCorsHeaders(httpContext, origin!);
                httpContext.Response.StatusCode = 204;
                return;
            }

            try
            {
                await next(context);
            }
            finally
            {
                // For actual cross-origin requests (GET/POST/PUT/DELETE etc.) from allowed
                // origins, ensure the CORS response headers are present on the way out.
                // (AuthMiddleware and action results do not set these.)
                if (isAllowedOrigin)
                {
                    SetCorsHeaders(httpContext, origin!);
                }
            }
        }

        private static bool IsAllowedOrigin(string? origin)
        {
            if (string.IsNullOrEmpty(origin)) return false;
            if (AllowedOrigins.Contains(origin)) return true;

            // Local dev: dotnet watch / VS may bind an alternate https://localhost port.
            if (origin.StartsWith("https://localhost:", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase))
                return true;

            // Azure Static Web Apps default hostnames (production custom domains should be listed
            // above or added via CORS env if needed).
            if (origin.EndsWith(".azurestaticapps.net", StringComparison.OrdinalIgnoreCase)
                && origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static void SetCorsHeaders(Microsoft.AspNetCore.Http.HttpContext httpContext, string origin)
        {
            httpContext.Response.Headers["Access-Control-Allow-Origin"] = origin;
            httpContext.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
            httpContext.Response.Headers["Access-Control-Allow-Headers"] =
                $"Authorization, Content-Type, {ImpersonateRoleHeader}";
            httpContext.Response.Headers["Access-Control-Allow-Credentials"] = "true";
        }
    }
}

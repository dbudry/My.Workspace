using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace My.Functions
{
    /// <summary>
    /// Catch-all OPTIONS handler so that CorsMiddleware runs for preflight requests.
    /// Without this, HttpTriggers that only declare "get"/"post"/etc never match OPTIONS,
    /// the worker middleware pipeline is never entered for preflights, and the browser
    /// sees "no ACAO header on preflight" even though the CorsMiddleware code is correct.
    ///
    /// Routing: more-specific routes win, so normal GET/POST etc still hit their functions.
    /// All OPTIONS (to any /api/* path) will hit this, middleware will see it, short-circuit
    /// with headers + 204, and never call into this (or auth).
    /// </summary>
    public static class PreflightFunction
    {
        [Function("CorsPreflight")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "{*path}")] HttpRequestData req)
        {
            // Real work happens in CorsMiddleware (headers + 204). We just need a function
            // to be selected so the middleware pipeline executes for this request.
            return new NoContentResult();
        }
    }
}

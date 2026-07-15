using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using My.DAL.Data;

namespace My.Functions
{
    /// <summary>
    /// Keeps a Function App instance warm on the Consumption plan and pings SQL
    /// so the connection pool / (if serverless) database resume path stays hot.
    ///
    /// Consumption plans deallocate the host after ~20 minutes of idleness; the next
    /// HTTP request then pays a 5-10 second cold-start penalty (rebuild assemblies,
    /// re-establish DB connection, etc.). A timer that fires inside the idle window
    /// keeps the host alive without upgrading to Premium / Always Ready.
    ///
    /// The SQL ping is a single lightweight connectivity check (<c>CanConnectAsync</c>).
    /// On provisioned Azure SQL this is negligible DTU. On serverless SKUs it also
    /// prevents auto-pause — see DEPLOYMENT.md notes on cost.
    /// </summary>
    public class KeepaliveFunction
    {
        private readonly ILogger<KeepaliveFunction> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public KeepaliveFunction(ILogger<KeepaliveFunction> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        // Runs every 5 minutes — well inside the ~20-minute idle deallocation window.
        // RunOnStartup=false so we don't pile up extra invocations on deploys.
        [Function("Keepalive")]
        public async Task Run([TimerTrigger("0 */5 * * * *", RunOnStartup = false)] TimerInfo timer)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var canConnect = await db.Database.CanConnectAsync();
                if (canConnect)
                {
                    _logger.LogDebug("Keepalive ping OK at {NowUtc:O} (SQL reachable).", DateTime.UtcNow);
                }
                else
                {
                    _logger.LogWarning("Keepalive ping at {NowUtc:O}: SQL CanConnectAsync returned false.", DateTime.UtcNow);
                }
            }
            catch (Exception ex)
            {
                // Never let keepalive failures recycle the host — log and move on.
                // A failed ping still exercised the function worker (host stays warm).
                _logger.LogWarning(ex, "Keepalive SQL ping failed at {NowUtc:O}. Host remains warm; SQL may still be cold on next request.", DateTime.UtcNow);
            }
        }
    }
}

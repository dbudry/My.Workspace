using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Repository;
using FluentValidation;
using My.Functions;
using My.Functions.Services;
using My.Shared.Validation;

var startupSw = Stopwatch.StartNew();
Console.WriteLine($"[STARTUP] Program.cs top-level statements entered at {DateTime.UtcNow:O}");

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(worker =>
    {
        worker.UseMiddleware<CorsMiddleware>(); // This is needed due to the type of azure function. It has some bugs and needs cors middleware along with built in cors.
        worker.UseMiddleware<AuthMiddleware>();
        worker.UseMiddleware<RateLimitMiddleware>();
    })
    .ConfigureServices((context, services) =>
    {

        // DbContext with SQL connection from env/config
        var connectionString = context.Configuration.GetConnectionString("DefaultConnection") ??
                                       Environment.GetEnvironmentVariable("DefaultConnection");
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions =>
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null))
            .ConfigureWarnings(warnings =>
                // In local dev with background auto-migrate, ignore this so we don't treat
                // "you should add a migration" as a fatal exception in the catch-all below.
                // You must still add migrations for real model changes before deploying.
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

        // IdentityCore for user/role management (remove if not querying users/roles in Functions)
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.SignIn.RequireConfirmedAccount = false;
        })
        .AddRoles<ApplicationRole>()
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        // HttpClient for auth token validation + memory cache for token results
        services.AddHttpClient();
        services.AddMemoryCache();

        // Essential DI (repository and mapper)
        services.AddTransient<IRepositoryFactory, RepositoryFactory>();
        services.AddSingleton<AppMapper>();

        // FluentValidation — all API request-shape guards before business rules / DB writes.
        // Singleton: validators are stateless; safe for Azure Functions isolated worker
        // (function classes are transient; avoids scoped-from-root issues on cold start).
        services.AddValidatorsFromAssemblyContaining<CreateStopwatchItemDtoValidator>(ServiceLifetime.Singleton);

        // Google Calendar integration
        services.AddSingleton<GoogleTokenEncryptor>();
        services.AddSingleton<GoogleCalendarService>();
        // Scoped because it depends on the DbContext (per-request).
        services.AddScoped<TeamAvailabilityPublisher>();

        // Google Drive integration for intranet (create/upload docs from page builder)
        services.AddSingleton<GoogleDriveService>();

        // Token credential for the admin Logs page. DefaultAzureCredential picks up the
        // Function App's managed identity in Azure and falls back to local dev credentials
        // (az login / Visual Studio sign-in) when running locally. LogsFunction uses this
        // to acquire bearer tokens for the App Insights Query API. The MI must be assigned
        // "Monitoring Reader" on the Application Insights resource — see README.
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());

        // JSON options if needed for enum/case handling in responses (else remove)
        services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        });
    })
    .ConfigureLogging(logging =>
    {
        logging.AddConsole(); // Basic console logging
    })
    .Build();

startupSw.Stop();
Console.WriteLine($"[STARTUP] Host built in {startupSw.ElapsedMilliseconds} ms. Now queuing any dev-only work and calling host.Run()...");

// In local development (when running `func start`), automatically apply any pending
// migrations to the configured database (e.g. your local dev DB). This makes the
// "local build" attempt to keep the schema in sync without manually running
// `dotnet ef database update` every time. 
// 
// SAFETY IMPROVEMENTS (updated):
// - Only runs if there are actually pending migrations.
// - Runs in a background Task (fire-and-forget) so it does NOT block the .NET worker
//   from reporting "ready" to the Functions host via gRPC. This prevents the
//   "Starting worker process failed / operation has timed out" and "No job functions found"
//   errors you were seeing when migration took a long time (especially first run with many tables).
// - Fully wrapped in try/catch + background so even a LocalDB access violation or long
//   migration will never crash or recycle the host/worker.
// - Uses MigrateAsync().
// - Clear [DB] logging.
// 
// Trade-off: The very first requests after `func start` could theoretically hit a
// "table not found" for a second or two if the background migration is still running.
// In practice this is rare and only on first cold start with a brand new dev DB.
// 
// IMPORTANT: This only runs in Development environment. Production relies on
// explicit migrations applied by the pipeline (never auto-migrate in prod).
if (host.Services.GetService<IHostEnvironment>()?.IsDevelopment() == true)
{
    // IMPORTANT: Fire-and-forget so worker startup is not blocked.
    // The main thread will quickly reach host.Run() and the worker will be able to
    // register all functions (including the new Intranet ones) with the host.
    _ = Task.Run(async () =>
    {
        var bgSw = Stopwatch.StartNew();
        try
        {
            using var scope = host.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var pendingMigrations = dbContext.Database.GetPendingMigrations().ToList();
            if (pendingMigrations.Any())
            {
                Console.WriteLine($"[DB] Applying {pendingMigrations.Count} pending migration(s) in background...");
                await dbContext.Database.MigrateAsync();
                Console.WriteLine($"[DB] Background migrations applied successfully (took {bgSw.ElapsedMilliseconds} ms).");
            }
            else
            {
                Console.WriteLine("[DB] No pending migrations.");
            }
        }
        catch (Exception ex)
        {
            // Never let migration problems kill the worker or host.
            Console.WriteLine($"[DB] WARNING: Background migration failed: {ex.Message}");
            Console.WriteLine("[DB] The application will continue. If needed, run manually:");
            Console.WriteLine("      dotnet ef database update --project My.DAL --startup-project My.AzureFunction");
        }
        finally
        {
            bgSw.Stop();
        }
    });
}

Console.WriteLine("[STARTUP] About to call host.Run() — this is where the worker reports ready to the Functions host.");
host.Run();

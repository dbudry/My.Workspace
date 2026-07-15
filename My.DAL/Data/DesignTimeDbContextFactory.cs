using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace My.DAL.Data;

/// <summary>
/// Used by EF Core tools (dotnet ef) to create the DbContext at design time.
/// Resolution order:
///   1. DefaultConnection environment variable
///   2. My.AzureFunction/local.settings.json (local dev)
///   3. Azure SQL with Active Directory Interactive (CI / remote fallback)
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private const string AzureFallback =
        "Server=your-sql.database.windows.net;Database=MyWorkspace;Authentication=Active Directory Interactive;Encrypt=True;TrustServerCertificate=False;";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString();

        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        builder.UseSqlServer(connectionString, sqlOptions =>
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 10,
                maxRetryDelay: TimeSpan.FromSeconds(60),
                errorNumbersToAdd: null));
        return new ApplicationDbContext(builder.Options);
    }

    private static string ResolveConnectionString()
    {
        var fromEnv = Environment.GetEnvironmentVariable("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        var fromLocalSettings = TryReadLocalSettingsConnectionString();
        if (!string.IsNullOrWhiteSpace(fromLocalSettings))
            return fromLocalSettings;

        return AzureFallback;
    }

    private static string? TryReadLocalSettingsConnectionString()
    {
        try
        {
            var baseDir = Directory.GetCurrentDirectory();
            var candidates = new[]
            {
                Path.Combine(baseDir, "local.settings.json"),
                Path.Combine(baseDir, "..", "My.AzureFunction", "local.settings.json"),
                Path.Combine(baseDir, "..", "..", "My.AzureFunction", "local.settings.json"),
            };

            foreach (var path in candidates)
            {
                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath)) continue;

                using var stream = File.OpenRead(fullPath);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs)
                    && cs.TryGetProperty("DefaultConnection", out var conn))
                {
                    var value = conn.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
        }
        catch
        {
            // Fall through to Azure fallback.
        }

        return null;
    }
}
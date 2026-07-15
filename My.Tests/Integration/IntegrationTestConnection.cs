using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using My.DAL.Data;

namespace My.Tests.Integration;

/// <summary>
/// Shared SQL Server connection for integration tests. Reads
/// <c>DefaultConnection</c> when set (CI + local override); otherwise uses the
/// Docker dev default on localhost:14333.
/// </summary>
public static class IntegrationTestConnection
{
    public const string DockerDevDefault =
        "Server=localhost,14333;Database=MyWorkspace_Dev;User Id=sa;Password=DevSql!Passw0rd2026;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("DefaultConnection") ?? DockerDevDefault;

    public static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    public static async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
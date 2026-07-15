using Xunit;

namespace My.Tests.Integration;

/// <summary>
/// Skips the test when the integration SQL Server is not reachable (e.g. local
/// <c>dotnet test</c> without Docker). CI provisions SQL before running tests.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!IntegrationTestConnection.CanConnectAsync().GetAwaiter().GetResult())
        {
            Skip = "SQL Server is not available. Start Docker SQL (Scripts/Dev-SetupDockerSql.ps1) or set DefaultConnection.";
        }
    }
}
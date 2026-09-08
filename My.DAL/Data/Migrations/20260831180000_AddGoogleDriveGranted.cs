using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using My.DAL.Data;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <summary>
    /// Intranet Drive is a separate Google consent from Calendar. Existing
    /// refresh tokens were issued with Drive on the old combined Connect.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260831180000_AddGoogleDriveGranted")]
    public partial class AddGoogleDriveGranted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Separate batches: SQL Server compiles a whole Sql() string at once, so
            // UPDATE in the same batch as ADD would fail with "Invalid column name".
            migrationBuilder.Sql("""
                ALTER TABLE [UserSettings] ADD [GoogleDriveGranted] bit NOT NULL
                    CONSTRAINT [DF_UserSettings_GoogleDriveGranted] DEFAULT 0;
                """);
            migrationBuilder.Sql("""
                UPDATE [UserSettings]
                SET [GoogleDriveGranted] = 1
                WHERE [GoogleRefreshToken] IS NOT NULL AND [GoogleRefreshToken] <> '';
                """);
            migrationBuilder.Sql("""
                ALTER TABLE [UserSettings] DROP CONSTRAINT [DF_UserSettings_GoogleDriveGranted];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE [UserSettings] DROP COLUMN [GoogleDriveGranted];
                """);
        }
    }
}

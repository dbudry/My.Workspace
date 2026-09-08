using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using My.DAL.Data;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <summary>
    /// Intranet Drive is a separate Google consent from Calendar.
    /// Column is already on UserSettings in InitialMigration for greenfield installs;
    /// this migration only adds + backfills when upgrading an older database.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260831180000_AddGoogleDriveGranted")]
    public partial class AddGoogleDriveGranted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Separate batches: SQL Server compiles a whole Sql() string at once.
            migrationBuilder.Sql("""
                IF COL_LENGTH('UserSettings', 'GoogleDriveGranted') IS NULL
                BEGIN
                    ALTER TABLE [UserSettings] ADD [GoogleDriveGranted] bit NOT NULL
                        CONSTRAINT [DF_UserSettings_GoogleDriveGranted] DEFAULT 0;
                END
                """);
            migrationBuilder.Sql("""
                IF COL_LENGTH('UserSettings', 'GoogleDriveGranted') IS NOT NULL
                   AND COL_LENGTH('UserSettings', 'GoogleRefreshToken') IS NOT NULL
                BEGIN
                    UPDATE [UserSettings]
                    SET [GoogleDriveGranted] = 1
                    WHERE [GoogleRefreshToken] IS NOT NULL AND [GoogleRefreshToken] <> ''
                      AND [GoogleDriveGranted] = 0;
                END
                """);
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[DF_UserSettings_GoogleDriveGranted]', N'D') IS NOT NULL
                    ALTER TABLE [UserSettings] DROP CONSTRAINT [DF_UserSettings_GoogleDriveGranted];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('UserSettings', 'GoogleDriveGranted') IS NOT NULL
                    ALTER TABLE [UserSettings] DROP COLUMN [GoogleDriveGranted];
                """);
        }
    }
}

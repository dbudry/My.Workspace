using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using My.DAL.Data;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <summary>
    /// Already on UserSettings in InitialMigration for greenfield; conditional add for upgrades.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260902150000_AddGoogleCalendarRemoveExcludedEvents")]
    public partial class AddGoogleCalendarRemoveExcludedEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('UserSettings', 'GoogleCalendarRemoveExcludedEvents') IS NULL
                BEGIN
                    ALTER TABLE [UserSettings] ADD [GoogleCalendarRemoveExcludedEvents] bit NOT NULL
                        CONSTRAINT [DF_UserSettings_GoogleCalendarRemoveExcludedEvents] DEFAULT 0;
                END
                """);
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[DF_UserSettings_GoogleCalendarRemoveExcludedEvents]', N'D') IS NOT NULL
                    ALTER TABLE [UserSettings] DROP CONSTRAINT [DF_UserSettings_GoogleCalendarRemoveExcludedEvents];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('UserSettings', 'GoogleCalendarRemoveExcludedEvents') IS NOT NULL
                    ALTER TABLE [UserSettings] DROP COLUMN [GoogleCalendarRemoveExcludedEvents];
                """);
        }
    }
}

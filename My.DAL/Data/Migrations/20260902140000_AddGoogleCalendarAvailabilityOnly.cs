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
    [Migration("20260902140000_AddGoogleCalendarAvailabilityOnly")]
    public partial class AddGoogleCalendarAvailabilityOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('UserSettings', 'GoogleCalendarAvailabilityOnly') IS NULL
                BEGIN
                    ALTER TABLE [UserSettings] ADD [GoogleCalendarAvailabilityOnly] bit NOT NULL
                        CONSTRAINT [DF_UserSettings_GoogleCalendarAvailabilityOnly] DEFAULT 0;
                END
                """);
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[DF_UserSettings_GoogleCalendarAvailabilityOnly]', N'D') IS NOT NULL
                    ALTER TABLE [UserSettings] DROP CONSTRAINT [DF_UserSettings_GoogleCalendarAvailabilityOnly];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('UserSettings', 'GoogleCalendarAvailabilityOnly') IS NOT NULL
                    ALTER TABLE [UserSettings] DROP COLUMN [GoogleCalendarAvailabilityOnly];
                """);
        }
    }
}

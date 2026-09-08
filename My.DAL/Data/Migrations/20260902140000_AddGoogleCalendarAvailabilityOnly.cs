using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using My.DAL.Data;

#nullable disable

namespace My.DAL.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260902140000_AddGoogleCalendarAvailabilityOnly")]
    public partial class AddGoogleCalendarAvailabilityOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE [UserSettings] ADD [GoogleCalendarAvailabilityOnly] bit NOT NULL
                    CONSTRAINT [DF_UserSettings_GoogleCalendarAvailabilityOnly] DEFAULT 0;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE [UserSettings] DROP CONSTRAINT [DF_UserSettings_GoogleCalendarAvailabilityOnly];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE [UserSettings] DROP COLUMN [GoogleCalendarAvailabilityOnly];
                """);
        }
    }
}

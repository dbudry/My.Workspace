using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using My.DAL.Data;

#nullable disable

namespace My.DAL.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260902150000_AddGoogleCalendarRemoveExcludedEvents")]
    public partial class AddGoogleCalendarRemoveExcludedEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE [UserSettings] ADD [GoogleCalendarRemoveExcludedEvents] bit NOT NULL
                    CONSTRAINT [DF_UserSettings_GoogleCalendarRemoveExcludedEvents] DEFAULT 0;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE [UserSettings] DROP CONSTRAINT [DF_UserSettings_GoogleCalendarRemoveExcludedEvents];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE [UserSettings] DROP COLUMN [GoogleCalendarRemoveExcludedEvents];
                """);
        }
    }
}

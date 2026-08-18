using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using My.DAL.Data;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <summary>
    /// Lets inbound Google Calendar sync tell a genuine user edit apart from the webhook
    /// echo of Tyme's own push ΓÇö see TrackedTask.GoogleEventUpdatedUtc and
    /// GoogleCalendarFunction.TryImportEventAsync.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260817200000_AddTrackedTaskGoogleEventUpdatedUtc")]
    public partial class AddTrackedTaskGoogleEventUpdatedUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // GoogleEventUpdatedUtc is created in InitialMigration for greenfield installs.
            // This migration is intentionally empty so the history chain stays intact.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

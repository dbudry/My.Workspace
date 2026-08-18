using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using My.DAL.Data;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <summary>
    /// StopwatchItems.IsCleared is created with the StopwatchItems table in
    /// AddStopwatchItems for greenfield installs. This migration is intentionally empty
    /// so the history chain stays intact.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260818150000_AddStopwatchItemIsCleared")]
    public partial class AddStopwatchItemIsCleared : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

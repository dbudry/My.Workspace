using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using My.DAL.Data;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <summary>
    /// TymeAllowUserTeamReports and Admin:Organizations are seeded in InitialMigration /
    /// ApplicationDbContext.HasData for greenfield installs. This migration is intentionally
    /// empty so the history chain stays intact.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260826120000_AddTymeAllowUserTeamReports")]
    public partial class AddTymeAllowUserTeamReports : Migration
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

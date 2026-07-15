using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectIsBillable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBillable",
                table: "Projects",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Legacy billings imported with TrackedTask.IsBillable = 1 — mark their
            // projects billable so existing client work stays consistent. Availability/PTO
            // projects are never billable regardless of historical task flags.
            migrationBuilder.Sql("""
                UPDATE p
                SET p.IsBillable = 1
                FROM Projects p
                WHERE p.IsSharedAvailability = 0
                  AND EXISTS (
                      SELECT 1
                      FROM TrackedTasks t
                      WHERE t.ProjectId = p.ProjectId
                        AND t.IsBillable = 1
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBillable",
                table: "Projects");
        }
    }
}

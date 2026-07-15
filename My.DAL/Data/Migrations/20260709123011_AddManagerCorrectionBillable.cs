using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManagerCorrectionBillable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NewIsBillable",
                table: "TrackedTaskCorrectionAudits",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PreviousIsBillable",
                table: "TrackedTaskCorrectionAudits",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsBillable",
                table: "TrackedTaskAliases",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE a
                SET a.IsBillable = t.IsBillable
                FROM TrackedTaskAliases a
                INNER JOIN TrackedTasks t ON t.TaskId = a.TaskId;
                """);

            migrationBuilder.Sql("""
                UPDATE audit
                SET audit.PreviousIsBillable = t.IsBillable,
                    audit.NewIsBillable = t.IsBillable
                FROM TrackedTaskCorrectionAudits audit
                INNER JOIN TrackedTasks t ON t.TaskId = audit.TaskId;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewIsBillable",
                table: "TrackedTaskCorrectionAudits");

            migrationBuilder.DropColumn(
                name: "PreviousIsBillable",
                table: "TrackedTaskCorrectionAudits");

            migrationBuilder.DropColumn(
                name: "IsBillable",
                table: "TrackedTaskAliases");
        }
    }
}

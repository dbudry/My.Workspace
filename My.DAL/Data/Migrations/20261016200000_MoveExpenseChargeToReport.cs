using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveExpenseChargeToReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChargeToName",
                table: "ExpenseReports",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChargeToOrganizationId",
                table: "ExpenseReports",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE r
                SET ChargeToOrganizationId = picked.ChargeToOrganizationId,
                    ChargeToName = picked.ChargeToName
                FROM ExpenseReports r
                CROSS APPLY (
                    SELECT TOP (1) l.ChargeToOrganizationId, l.ChargeToName
                    FROM ExpenseLines l
                    WHERE l.ExpenseReportId = r.ExpenseReportId
                      AND l.ChargeToOrganizationId IS NOT NULL
                      AND LTRIM(RTRIM(l.ChargeToOrganizationId)) <> ''
                    ORDER BY l.SortOrder, l.ExpenseLineId
                ) picked
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseLines_Organizations_ChargeToOrganizationId",
                table: "ExpenseLines");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseLines_ChargeToOrganizationId",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "ChargeToName",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "ChargeToOrganizationId",
                table: "ExpenseLines");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseReports_ChargeToOrganizationId",
                table: "ExpenseReports",
                column: "ChargeToOrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseReports_Organizations_ChargeToOrganizationId",
                table: "ExpenseReports",
                column: "ChargeToOrganizationId",
                principalTable: "Organizations",
                principalColumn: "OrganizationId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseReports_Organizations_ChargeToOrganizationId",
                table: "ExpenseReports");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseReports_ChargeToOrganizationId",
                table: "ExpenseReports");

            migrationBuilder.AddColumn<string>(
                name: "ChargeToName",
                table: "ExpenseLines",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChargeToOrganizationId",
                table: "ExpenseLines",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseLines_ChargeToOrganizationId",
                table: "ExpenseLines",
                column: "ChargeToOrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseLines_Organizations_ChargeToOrganizationId",
                table: "ExpenseLines",
                column: "ChargeToOrganizationId",
                principalTable: "Organizations",
                principalColumn: "OrganizationId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.DropColumn(
                name: "ChargeToName",
                table: "ExpenseReports");

            migrationBuilder.DropColumn(
                name: "ChargeToOrganizationId",
                table: "ExpenseReports");
        }
    }
}

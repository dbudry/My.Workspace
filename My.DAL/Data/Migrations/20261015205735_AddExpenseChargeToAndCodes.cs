using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseChargeToAndCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MealBreakfastCount",
                table: "ExpenseLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MealDinnerCount",
                table: "ExpenseLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MealLunchCount",
                table: "ExpenseLines",
                type: "int",
                nullable: true);

            // One meal per line. Breakfast wins, then lunch, then dinner. A checked box becomes headcount 1.
            migrationBuilder.Sql("""
                UPDATE ExpenseLines
                SET MealBreakfastCount = CASE WHEN MealBreakfast = 1 THEN 1 ELSE NULL END,
                    MealLunchCount = CASE WHEN MealBreakfast = 0 AND MealLunch = 1 THEN 1 ELSE NULL END,
                    MealDinnerCount = CASE WHEN MealBreakfast = 0 AND MealLunch = 0 AND MealDinner = 1 THEN 1 ELSE NULL END
                """);

            migrationBuilder.DropColumn(
                name: "MealBreakfast",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "MealDinner",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "MealLunch",
                table: "ExpenseLines");

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

            migrationBuilder.CreateTable(
                name: "ExpenseCodes",
                columns: table => new
                {
                    ExpenseCodeId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCodes", x => x.ExpenseCodeId);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseFiledPdfs",
                columns: table => new
                {
                    ExpenseFiledPdfId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExpenseReportId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ChargeToOrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ChargeToName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DriveFileId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseFiledPdfs", x => x.ExpenseFiledPdfId);
                    table.ForeignKey(
                        name: "FK_ExpenseFiledPdfs_ExpenseReports_ExpenseReportId",
                        column: x => x.ExpenseReportId,
                        principalTable: "ExpenseReports",
                        principalColumn: "ExpenseReportId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseLines_ChargeToOrganizationId",
                table: "ExpenseLines",
                column: "ChargeToOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCodes_Kind_Code",
                table: "ExpenseCodes",
                columns: new[] { "Kind", "Code" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO ExpenseCodes (ExpenseCodeId, Kind, Code, Label, IsActive, IsLocked, SortOrder) VALUES
                ('e7c0a001000000000000000000000001', 'Transportation', 'M', 'Personal car', 1, 1, 1),
                ('e7c0a001000000000000000000000002', 'Transportation', 'A', 'Rented car', 1, 1, 2),
                ('e7c0a001000000000000000000000003', 'Transportation', 'B', 'Plane ticket', 1, 1, 3),
                ('e7c0a001000000000000000000000004', 'Transportation', 'C', 'Local fares', 1, 1, 4),
                ('e7c0a001000000000000000000000005', 'Transportation', 'F', 'Tolls', 1, 1, 5),
                ('e7c0a001000000000000000000000006', 'Transportation', 'P', 'Parking', 1, 1, 6),
                ('e7c0a001000000000000000000000007', 'Transportation', 'R', 'Rail ticket', 1, 1, 7),
                ('e7c0a001000000000000000000000008', 'Transportation', 'T', 'Airport tax', 1, 1, 8),
                ('e7c0a001000000000000000000000009', 'Transportation', 'W', 'Gas', 1, 1, 9),
                ('e7c0a00100000000000000000000000a', 'Transportation', 'X', 'Excess weight', 1, 1, 10),
                ('e7c0a00100000000000000000000000b', 'Transportation', 'Y', 'Other', 1, 1, 11),
                ('e7c0a00100000000000000000000000c', 'Miscellaneous', 'G', 'Tips', 1, 0, 12),
                ('e7c0a00100000000000000000000000d', 'Miscellaneous', 'H', 'Postage', 1, 0, 13),
                ('e7c0a00100000000000000000000000e', 'Miscellaneous', 'L', 'Laundry', 1, 0, 14),
                ('e7c0a00100000000000000000000000f', 'Miscellaneous', 'V', 'Valet', 1, 0, 15),
                ('e7c0a001000000000000000000000010', 'Miscellaneous', 'Z', 'Other', 1, 0, 16)
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseFiledPdfs_ExpenseReportId",
                table: "ExpenseFiledPdfs",
                column: "ExpenseReportId");

            migrationBuilder.Sql("""
                INSERT INTO ExpenseFiledPdfs (ExpenseFiledPdfId, ExpenseReportId, ChargeToOrganizationId, ChargeToName, DriveFileId, SortOrder)
                SELECT REPLACE(CONVERT(varchar(36), NEWID()), '-', ''),
                       ExpenseReportId,
                       NULL,
                       LEFT(ISNULL(NULLIF(LTRIM(RTRIM(ChargeToNote)), ''), ''), 100),
                       DriveFiledPdfFileId,
                       0
                FROM ExpenseReports
                WHERE DriveFiledPdfFileId IS NOT NULL AND LTRIM(RTRIM(DriveFiledPdfFileId)) <> ''
                """);

            migrationBuilder.DropColumn(
                name: "DriveFiledPdfFileId",
                table: "ExpenseReports");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseLines_Organizations_ChargeToOrganizationId",
                table: "ExpenseLines",
                column: "ChargeToOrganizationId",
                principalTable: "Organizations",
                principalColumn: "OrganizationId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseLines_Organizations_ChargeToOrganizationId",
                table: "ExpenseLines");

            migrationBuilder.AddColumn<string>(
                name: "DriveFiledPdfFileId",
                table: "ExpenseReports",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE r
                SET DriveFiledPdfFileId = picked.DriveFileId
                FROM ExpenseReports r
                INNER JOIN (
                    SELECT ExpenseReportId, DriveFileId,
                           ROW_NUMBER() OVER (PARTITION BY ExpenseReportId ORDER BY SortOrder, ExpenseFiledPdfId) AS rn
                    FROM ExpenseFiledPdfs
                ) picked ON picked.ExpenseReportId = r.ExpenseReportId AND picked.rn = 1
                """);

            migrationBuilder.DropTable(
                name: "ExpenseCodes");

            migrationBuilder.DropTable(
                name: "ExpenseFiledPdfs");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseLines_ChargeToOrganizationId",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "ChargeToName",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "ChargeToOrganizationId",
                table: "ExpenseLines");

            migrationBuilder.AddColumn<bool>(
                name: "MealBreakfast",
                table: "ExpenseLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MealDinner",
                table: "ExpenseLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MealLunch",
                table: "ExpenseLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE ExpenseLines
                SET MealBreakfast = CASE WHEN MealBreakfastCount IS NOT NULL THEN 1 ELSE 0 END,
                    MealLunch = CASE WHEN MealBreakfastCount IS NULL AND MealLunchCount IS NOT NULL THEN 1 ELSE 0 END,
                    MealDinner = CASE WHEN MealBreakfastCount IS NULL AND MealLunchCount IS NULL AND MealDinnerCount IS NOT NULL THEN 1 ELSE 0 END
                """);

            migrationBuilder.DropColumn(
                name: "MealBreakfastCount",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "MealDinnerCount",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "MealLunchCount",
                table: "ExpenseLines");
        }
    }
}

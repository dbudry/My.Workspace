using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    public partial class AddExpenseFiledPdfDriveFileId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('ExpenseReports', 'DriveFiledPdfFileId') IS NULL
                    ALTER TABLE [ExpenseReports] ADD [DriveFiledPdfFileId] nvarchar(128) NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('ExpenseReports', 'DriveFiledPdfFileId') IS NOT NULL
                    ALTER TABLE [ExpenseReports] DROP COLUMN [DriveFiledPdfFileId];
                """);
        }
    }
}

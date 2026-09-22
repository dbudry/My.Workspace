using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    public partial class AddExpenseReportReimbursed : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('ExpenseReports', 'ReimbursedAt') IS NULL
                    ALTER TABLE [ExpenseReports] ADD [ReimbursedAt] datetime2 NULL;
                """);
            migrationBuilder.Sql("""
                IF COL_LENGTH('ExpenseReports', 'ReimbursedByUserId') IS NULL
                    ALTER TABLE [ExpenseReports] ADD [ReimbursedByUserId] nvarchar(max) NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('ExpenseReports', 'ReimbursedAt') IS NOT NULL
                    ALTER TABLE [ExpenseReports] DROP COLUMN [ReimbursedAt];
                """);
            migrationBuilder.Sql("""
                IF COL_LENGTH('ExpenseReports', 'ReimbursedByUserId') IS NOT NULL
                    ALTER TABLE [ExpenseReports] DROP COLUMN [ReimbursedByUserId];
                """);
        }
    }
}

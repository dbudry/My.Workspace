using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    public partial class AddExpenseHomeAddress : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('UserSettings', 'ExpenseHomeAddress') IS NULL
                    ALTER TABLE [UserSettings] ADD [ExpenseHomeAddress] nvarchar(255) NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('UserSettings', 'ExpenseHomeAddress') IS NOT NULL
                    ALTER TABLE [UserSettings] DROP COLUMN [ExpenseHomeAddress];
                """);
        }
    }
}

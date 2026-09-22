using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    public partial class AddExpenseSignatures : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('UserSettings', 'ExpenseSignature') IS NULL
                    ALTER TABLE [UserSettings] ADD [ExpenseSignature] varbinary(max) NULL;
                """);
            migrationBuilder.Sql("""
                IF COL_LENGTH('UserSettings', 'ExpenseSignatureMime') IS NULL
                    ALTER TABLE [UserSettings] ADD [ExpenseSignatureMime] nvarchar(64) NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('UserSettings', 'ExpenseSignature') IS NOT NULL
                    ALTER TABLE [UserSettings] DROP COLUMN [ExpenseSignature];
                """);
            migrationBuilder.Sql("""
                IF COL_LENGTH('UserSettings', 'ExpenseSignatureMime') IS NOT NULL
                    ALTER TABLE [UserSettings] DROP COLUMN [ExpenseSignatureMime];
                """);
        }
    }
}

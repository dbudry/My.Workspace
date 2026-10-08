using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleExpenseReportsPerMonth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExpenseReports_UserId_Year_Month",
                table: "ExpenseReports");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseReports_UserId_Year_Month",
                table: "ExpenseReports",
                columns: new[] { "UserId", "Year", "Month" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExpenseReports_UserId_Year_Month",
                table: "ExpenseReports");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseReports_UserId_Year_Month",
                table: "ExpenseReports",
                columns: new[] { "UserId", "Year", "Month" },
                unique: true);
        }
    }
}

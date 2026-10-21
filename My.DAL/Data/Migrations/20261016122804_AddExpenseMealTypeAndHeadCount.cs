using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseMealTypeAndHeadCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MealType",
                table: "ExpenseLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeadCount",
                table: "ExpenseLines",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE ExpenseLines
                SET MealType = CASE
                        WHEN MealBreakfastCount IS NOT NULL THEN N'Breakfast'
                        WHEN MealLunchCount IS NOT NULL THEN N'Lunch'
                        WHEN MealDinnerCount IS NOT NULL THEN N'Dinner'
                        ELSE NULL
                    END,
                    HeadCount = COALESCE(MealBreakfastCount, MealLunchCount, MealDinnerCount)
                """);

            migrationBuilder.DropColumn(
                name: "MealBreakfastCount",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "MealLunchCount",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "MealDinnerCount",
                table: "ExpenseLines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MealBreakfastCount",
                table: "ExpenseLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MealLunchCount",
                table: "ExpenseLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MealDinnerCount",
                table: "ExpenseLines",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE ExpenseLines
                SET MealBreakfastCount = CASE WHEN MealType = N'Breakfast' THEN HeadCount ELSE NULL END,
                    MealLunchCount = CASE WHEN MealType = N'Lunch' THEN HeadCount ELSE NULL END,
                    MealDinnerCount = CASE WHEN MealType = N'Dinner' THEN HeadCount ELSE NULL END
                """);

            migrationBuilder.DropColumn(
                name: "MealType",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "HeadCount",
                table: "ExpenseLines");
        }
    }
}

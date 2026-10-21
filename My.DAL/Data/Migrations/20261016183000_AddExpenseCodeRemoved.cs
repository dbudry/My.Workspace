using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseCodeRemoved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRemoved",
                table: "ExpenseCodes",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRemoved",
                table: "ExpenseCodes");
        }
    }
}

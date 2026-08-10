using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDefaultStartTimeMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 480 = 08:00 wall clock — product default for new timed Tyme entries.
            migrationBuilder.AddColumn<int>(
                name: "DefaultStartTimeMinutes",
                table: "UserSettings",
                type: "int",
                nullable: false,
                defaultValue: 480);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultStartTimeMinutes",
                table: "UserSettings");
        }
    }
}

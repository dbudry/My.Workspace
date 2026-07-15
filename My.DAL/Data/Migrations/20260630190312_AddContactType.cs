using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContactType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactType",
                table: "Contacts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "Key", "Description", "Value" },
                values: new object[] { "ContactTypes", "JSON array of allowed contact types for organization and department contacts.", "[\"Primary\",\"Billing\",\"Maintenance\"]" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "ContactTypes");

            migrationBuilder.DropColumn(
                name: "ContactType",
                table: "Contacts");
        }
    }
}

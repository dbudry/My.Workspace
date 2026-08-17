using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackedTaskAliasNameAndManagerCorrectionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TrackedTaskAliases.Details is created in InitialMigration for greenfield installs.
            // This migration only seeds manager-correction AppSettings.

            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "Key", "Description", "Value" },
                values: new object[,]
                {
                    { "TymeAllowManagerTimeCorrection", "When enabled, Tyme managers and admins can correct submitted employee time entries.", "true" },
                    { "TymeManagerCorrectionMode", "When manager correction is enabled: Alias (overlay, original preserved) or Direct (in-place edit with audit).", "Alias" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "TymeManagerCorrectionMode");

            migrationBuilder.DeleteData(
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "TymeAllowManagerTimeCorrection");
        }
    }
}

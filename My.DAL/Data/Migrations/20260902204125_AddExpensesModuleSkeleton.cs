using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpensesModuleSkeleton : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "Key", "Description", "Value" },
                values: new object[,]
                {
                    { "ExpensesDriveParentFolderId", "Google Drive folder ID for the private Expenses root. Not shared company-wide.", "" },
                    { "ExpensesMileageRatePerMile", "Personal-car mileage reimbursement rate in USD per mile.", "0.555" },
                    { "HomeOrganizationId", "OrganizationId of the home company. Expenses department pickers filter to this org.", "" }
                });

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Description", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { "10a1b2c3-d4e5-4678-9abc-def012345601", "10a1b2c3-d4e5-4678-9abc-def012345601", "Expenses-scoped user role (own reports and receipts).", "User:Expenses", "USER:EXPENSES" },
                    { "10a1b2c3-d4e5-4678-9abc-def012345602", "10a1b2c3-d4e5-4678-9abc-def012345602", "Expenses-scoped manager role (team reports and unsubmit).", "Manager:Expenses", "MANAGER:EXPENSES" },
                    { "10a1b2c3-d4e5-4678-9abc-def012345603", "10a1b2c3-d4e5-4678-9abc-def012345603", "Expenses-scoped admin role (module admin).", "Admin:Expenses", "ADMIN:EXPENSES" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "ExpensesDriveParentFolderId");

            migrationBuilder.DeleteData(
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "ExpensesMileageRatePerMile");

            migrationBuilder.DeleteData(
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "HomeOrganizationId");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "10a1b2c3-d4e5-4678-9abc-def012345601");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "10a1b2c3-d4e5-4678-9abc-def012345602");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "10a1b2c3-d4e5-4678-9abc-def012345603");
        }
    }
}

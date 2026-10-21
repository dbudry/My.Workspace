using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseCurrenciesAndCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExpenseCurrencyLayout",
                table: "UserSettings",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Mixed");

            migrationBuilder.AddColumn<string>(
                name: "ExpenseDefaultCurrencyCode",
                table: "UserSettings",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<string>(
                name: "ExpenseEditorView",
                table: "UserSettings",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Detail");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "ExpenseLines",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToUsd",
                table: "ExpenseLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedAt",
                table: "ExpenseLines",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SpentAmount",
                table: "ExpenseLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("UPDATE ExpenseLines SET SpentAmount = Amount");

            migrationBuilder.CreateTable(
                name: "ExpenseCurrencies",
                columns: table => new
                {
                    ExpenseCurrencyId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RateToUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsRemoved = table.Column<bool>(type: "bit", nullable: false),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCurrencies", x => x.ExpenseCurrencyId);
                });

            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "Key", "Description", "Value" },
                values: new object[] { "ExpensesCreditBucketEnabled", "When true, a returned paid expense line becomes a credit against later expenses.", "false" });

            migrationBuilder.InsertData(
                table: "ExpenseCurrencies",
                columns: new[] { "ExpenseCurrencyId", "Code", "Color", "IsActive", "IsLocked", "IsRemoved", "Name", "RateToUsd", "SortOrder" },
                values: new object[,]
                {
                    { "e5c00000000000000000000000000001", "USD", "#1565C0", true, true, false, "US Dollar", 1m, 0 },
                    { "e5c00000000000000000000000000002", "EUR", "#F9A825", true, false, false, "Euro", 1m, 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCurrencies_Code",
                table: "ExpenseCurrencies",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpenseCurrencies");

            migrationBuilder.DeleteData(
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "ExpensesCreditBucketEnabled");

            migrationBuilder.DropColumn(
                name: "ExpenseCurrencyLayout",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "ExpenseDefaultCurrencyCode",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "ExpenseEditorView",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToUsd",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "ReturnedAt",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "SpentAmount",
                table: "ExpenseLines");
        }
    }
}

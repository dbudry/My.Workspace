using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpensePartialCreditAndFormCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ExpenseDefaultCurrencyCode",
                table: "UserSettings",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(8)",
                oldMaxLength: 8,
                oldDefaultValue: "USD");

            migrationBuilder.AddColumn<decimal>(
                name: "CreditAmount",
                table: "ExpenseLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditCurrencyCode",
                table: "ExpenseLines",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditDescription",
                table: "ExpenseLines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditExchangeRate",
                table: "ExpenseLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExpenseCredits",
                columns: table => new
                {
                    ExpenseCreditId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExpenseReportId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ChargeToOrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreditDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    ExchangeRateToUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCredits", x => x.ExpenseCreditId);
                    table.ForeignKey(
                        name: "FK_ExpenseCredits_ExpenseReports_ExpenseReportId",
                        column: x => x.ExpenseReportId,
                        principalTable: "ExpenseReports",
                        principalColumn: "ExpenseReportId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCredits_ExpenseReportId",
                table: "ExpenseCredits",
                column: "ExpenseReportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpenseCredits");

            migrationBuilder.DropColumn(
                name: "CreditAmount",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "CreditCurrencyCode",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "CreditDescription",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "CreditExchangeRate",
                table: "ExpenseLines");

            migrationBuilder.AlterColumn<string>(
                name: "ExpenseDefaultCurrencyCode",
                table: "UserSettings",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "USD",
                oldClrType: typeof(string),
                oldType: "nvarchar(8)",
                oldMaxLength: 8,
                oldNullable: true);
        }
    }
}

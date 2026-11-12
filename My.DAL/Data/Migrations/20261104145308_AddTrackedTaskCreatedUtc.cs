using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackedTaskCreatedUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedUtc",
                table: "TrackedTasks",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedUtc",
                table: "TrackedTasks");
        }
    }
}

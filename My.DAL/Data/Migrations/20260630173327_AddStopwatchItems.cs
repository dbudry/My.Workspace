using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStopwatchItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StopwatchItemId",
                table: "TrackedTasks",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StopwatchItems",
                columns: table => new
                {
                    StopwatchItemId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProjectId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    LastWorkedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StopwatchItems", x => x.StopwatchItemId);
                    table.ForeignKey(
                        name: "FK_StopwatchItems_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StopwatchItems_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedTasks_StopwatchItemId_StartDate",
                table: "TrackedTasks",
                columns: new[] { "StopwatchItemId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StopwatchItems_ProjectId",
                table: "StopwatchItems",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_StopwatchItems_UserId_LastWorkedAt",
                table: "StopwatchItems",
                columns: new[] { "UserId", "LastWorkedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_TrackedTasks_StopwatchItems_StopwatchItemId",
                table: "TrackedTasks",
                column: "StopwatchItemId",
                principalTable: "StopwatchItems",
                principalColumn: "StopwatchItemId",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrackedTasks_StopwatchItems_StopwatchItemId",
                table: "TrackedTasks");

            migrationBuilder.DropTable(
                name: "StopwatchItems");

            migrationBuilder.DropIndex(
                name: "IX_TrackedTasks_StopwatchItemId_StartDate",
                table: "TrackedTasks");

            migrationBuilder.DropColumn(
                name: "StopwatchItemId",
                table: "TrackedTasks");
        }
    }
}

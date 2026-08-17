using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackedTaskCorrectionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackedTaskCorrectionAudits",
                columns: table => new
                {
                    TrackedTaskCorrectionAuditId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TaskId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CorrectedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreviousDetails = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreviousStartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreviousDuration = table.Column<TimeSpan>(type: "time", nullable: false),
                    PreviousEndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PreviousIsAllDay = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    PreviousProjectId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreviousIsBillable = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    NewDetails = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NewStartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NewDuration = table.Column<TimeSpan>(type: "time", nullable: false),
                    NewProjectId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewIsBillable = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedTaskCorrectionAudits", x => x.TrackedTaskCorrectionAuditId);
                    table.ForeignKey(
                        name: "FK_TrackedTaskCorrectionAudits_TrackedTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TrackedTasks",
                        principalColumn: "TaskId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedTaskCorrectionAudits_TaskId",
                table: "TrackedTaskCorrectionAudits",
                column: "TaskId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackedTaskCorrectionAudits");
        }
    }
}

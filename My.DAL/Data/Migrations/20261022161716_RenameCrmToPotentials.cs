using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameCrmToPotentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmActivities");

            migrationBuilder.CreateTable(
                name: "CrmStages",
                columns: table => new
                {
                    CrmStageId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DefaultProbability = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmStages", x => x.CrmStageId);
                });

            migrationBuilder.InsertData(
                table: "CrmStages",
                columns: new[] { "CrmStageId", "Name", "DefaultProbability", "Outcome", "SortOrder" },
                values: new object[,]
                {
                    { "11150000-0000-4000-8000-000000000001", "Lead", 5, "Open", 0 },
                    { "11150000-0000-4000-8000-000000000002", "Qualified", 10, "Open", 1 },
                    { "11150000-0000-4000-8000-000000000003", "Value Proposition", 30, "Open", 2 },
                    { "11150000-0000-4000-8000-000000000004", "Proposal", 50, "Open", 3 },
                    { "11150000-0000-4000-8000-000000000005", "Negotiation", 70, "Open", 4 },
                    { "11150000-0000-4000-8000-000000000006", "Closed Won", 100, "Closed Won", 5 },
                    { "11150000-0000-4000-8000-000000000007", "Closed Lost", 0, "Closed Lost", 6 }
                });

            migrationBuilder.DropIndex(
                name: "IX_Opportunities_Stage",
                table: "Opportunities");

            migrationBuilder.RenameTable(
                name: "Opportunities",
                newName: "Potentials");

            migrationBuilder.RenameIndex(
                name: "IX_Opportunities_OrganizationId",
                table: "Potentials",
                newName: "IX_Potentials_OrganizationId");

            migrationBuilder.RenameColumn(
                name: "OpportunityId",
                table: "Potentials",
                newName: "PotentialId");

            migrationBuilder.RenameColumn(
                name: "Note",
                table: "Potentials",
                newName: "Description");

            migrationBuilder.Sql("EXEC sp_rename N'PK_Opportunities', N'PK_Potentials', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'FK_Opportunities_Organizations_OrganizationId', N'FK_Potentials_Organizations_OrganizationId', N'OBJECT';");

            migrationBuilder.AddColumn<string>(
                name: "CrmStageId",
                table: "Potentials",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Probability",
                table: "Potentials",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualCloseDate",
                table: "Potentials",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Potentials",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedByUserId",
                table: "Potentials",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE Potentials
                SET CrmStageId = CASE Stage
                    WHEN 'Lead' THEN '11150000-0000-4000-8000-000000000001'
                    WHEN 'Qualified' THEN '11150000-0000-4000-8000-000000000002'
                    WHEN 'Proposal' THEN '11150000-0000-4000-8000-000000000004'
                    WHEN 'Negotiation' THEN '11150000-0000-4000-8000-000000000005'
                    WHEN 'Won' THEN '11150000-0000-4000-8000-000000000006'
                    WHEN 'Lost' THEN '11150000-0000-4000-8000-000000000007'
                    ELSE '11150000-0000-4000-8000-000000000001'
                END;

                UPDATE p
                SET Probability = s.DefaultProbability,
                    CreatedByUserId = p.OwnerUserId,
                    ModifiedByUserId = p.OwnerUserId
                FROM Potentials p
                INNER JOIN CrmStages s ON s.CrmStageId = p.CrmStageId;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "CrmStageId",
                table: "Potentials",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Probability",
                table: "Potentials",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "Potentials");

            migrationBuilder.AddForeignKey(
                name: "FK_Potentials_CrmStages_CrmStageId",
                table: "Potentials",
                column: "CrmStageId",
                principalTable: "CrmStages",
                principalColumn: "CrmStageId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateTable(
                name: "CrmAttachments",
                columns: table => new
                {
                    CrmAttachmentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PotentialId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CrmStageId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    DriveFileId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmAttachments", x => x.CrmAttachmentId);
                    table.ForeignKey(
                        name: "FK_CrmAttachments_CrmStages_CrmStageId",
                        column: x => x.CrmStageId,
                        principalTable: "CrmStages",
                        principalColumn: "CrmStageId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmAttachments_Potentials_PotentialId",
                        column: x => x.PotentialId,
                        principalTable: "Potentials",
                        principalColumn: "PotentialId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrmDiscussions",
                columns: table => new
                {
                    CrmDiscussionId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PotentialId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CrmStageId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AuthorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmDiscussions", x => x.CrmDiscussionId);
                    table.ForeignKey(
                        name: "FK_CrmDiscussions_CrmStages_CrmStageId",
                        column: x => x.CrmStageId,
                        principalTable: "CrmStages",
                        principalColumn: "CrmStageId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmDiscussions_Potentials_PotentialId",
                        column: x => x.PotentialId,
                        principalTable: "Potentials",
                        principalColumn: "PotentialId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PotentialStatuses",
                columns: table => new
                {
                    PotentialStatusId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PotentialId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CrmStageId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StatusDate = table.Column<DateTime>(type: "date", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PotentialStatuses", x => x.PotentialStatusId);
                    table.ForeignKey(
                        name: "FK_PotentialStatuses_CrmStages_CrmStageId",
                        column: x => x.CrmStageId,
                        principalTable: "CrmStages",
                        principalColumn: "CrmStageId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PotentialStatuses_Potentials_PotentialId",
                        column: x => x.PotentialId,
                        principalTable: "Potentials",
                        principalColumn: "PotentialId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddColumn<string>(
                name: "RelationshipType",
                table: "Contacts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "Key", "Description", "Value" },
                values: new object[,]
                {
                    { "CrmDiscussionNotifyUserIds", "JSON array of user ids emailed when someone posts a CRM discussion.", "[]" },
                    { "CrmDriveParentFolderId", "Google Drive folder ID for the CRM root under the App Shared Drive.", "" },
                    { "CrmRelationshipTypes", "JSON array of CRM relationship types (name and description) stored on contacts.", "[{\"Name\":\"Decision Maker\",\"Description\":\"the person who can approve the purchase\"},{\"Name\":\"Influencer\",\"Description\":\"a person who shapes the decision and does not give final approval\"},{\"Name\":\"Assessor\",\"Description\":\"a person who evaluates fit, risk, or terms\"}]" }
                });

            migrationBuilder.UpdateData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "30a1b2c3-d4e5-4678-9abc-def012345801",
                column: "Description",
                value: "CRM-scoped user role (view the pipeline and post discussion).");

            migrationBuilder.UpdateData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "30a1b2c3-d4e5-4678-9abc-def012345802",
                column: "Description",
                value: "CRM-scoped editor role (create and edit potentials, contacts, status, and attachments).");

            migrationBuilder.UpdateData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "30a1b2c3-d4e5-4678-9abc-def012345803",
                column: "Description",
                value: "CRM-scoped manager role (archive, delete, and CRM settings).");

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Description", "Name", "NormalizedName" },
                values: new object[] { "30a1b2c3-d4e5-4678-9abc-def012345805", "30a1b2c3-d4e5-4678-9abc-def012345805", "CRM Analyst — read the pipeline, report, dashboard, and attachments. Cannot post, upload, or edit.", "Analyst:Crm", "ANALYST:CRM" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmAttachments_CrmStageId",
                table: "CrmAttachments",
                column: "CrmStageId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmAttachments_PotentialId",
                table: "CrmAttachments",
                column: "PotentialId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmDiscussions_CrmStageId",
                table: "CrmDiscussions",
                column: "CrmStageId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmDiscussions_PotentialId",
                table: "CrmDiscussions",
                column: "PotentialId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmStages_SortOrder",
                table: "CrmStages",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_Potentials_CrmStageId",
                table: "Potentials",
                column: "CrmStageId");

            migrationBuilder.CreateIndex(
                name: "IX_PotentialStatuses_CrmStageId",
                table: "PotentialStatuses",
                column: "CrmStageId");

            migrationBuilder.CreateIndex(
                name: "IX_PotentialStatuses_PotentialId",
                table: "PotentialStatuses",
                column: "PotentialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CrmAttachments");
            migrationBuilder.DropTable(name: "CrmDiscussions");
            migrationBuilder.DropTable(name: "PotentialStatuses");

            migrationBuilder.DropForeignKey(
                name: "FK_Potentials_CrmStages_CrmStageId",
                table: "Potentials");

            migrationBuilder.DropIndex(
                name: "IX_Potentials_CrmStageId",
                table: "Potentials");

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                table: "Potentials",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE p
                SET Stage = CASE s.Name
                    WHEN 'Closed Won' THEN 'Won'
                    WHEN 'Closed Lost' THEN 'Lost'
                    WHEN 'Lead' THEN 'Lead'
                    WHEN 'Qualified' THEN 'Qualified'
                    WHEN 'Proposal' THEN 'Proposal'
                    WHEN 'Negotiation' THEN 'Negotiation'
                    ELSE 'Lead'
                END
                FROM Potentials p
                LEFT JOIN CrmStages s ON s.CrmStageId = p.CrmStageId;

                UPDATE Potentials SET Stage = 'Lead' WHERE Stage IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Stage",
                table: "Potentials",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.DropColumn(name: "CrmStageId", table: "Potentials");
            migrationBuilder.DropColumn(name: "Probability", table: "Potentials");
            migrationBuilder.DropColumn(name: "ActualCloseDate", table: "Potentials");
            migrationBuilder.DropColumn(name: "CreatedByUserId", table: "Potentials");
            migrationBuilder.DropColumn(name: "ModifiedByUserId", table: "Potentials");

            migrationBuilder.RenameColumn(name: "PotentialId", table: "Potentials", newName: "OpportunityId");
            migrationBuilder.RenameColumn(name: "Description", table: "Potentials", newName: "Note");
            migrationBuilder.RenameIndex(
                name: "IX_Potentials_OrganizationId",
                table: "Potentials",
                newName: "IX_Opportunities_OrganizationId");
            migrationBuilder.Sql("EXEC sp_rename N'PK_Potentials', N'PK_Opportunities', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'FK_Potentials_Organizations_OrganizationId', N'FK_Opportunities_Organizations_OrganizationId', N'OBJECT';");
            migrationBuilder.RenameTable(name: "Potentials", newName: "Opportunities");

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_Stage",
                table: "Opportunities",
                column: "Stage");

            migrationBuilder.DropTable(name: "CrmStages");

            migrationBuilder.CreateTable(
                name: "CrmActivities",
                columns: table => new
                {
                    CrmActivityId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OpportunityId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ActivityType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmActivities", x => x.CrmActivityId);
                    table.ForeignKey(
                        name: "FK_CrmActivities_Opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "Opportunities",
                        principalColumn: "OpportunityId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmActivities_OpportunityId",
                table: "CrmActivities",
                column: "OpportunityId");

            migrationBuilder.DropColumn(name: "RelationshipType", table: "Contacts");

            migrationBuilder.DeleteData(table: "AppSettings", keyColumn: "Key", keyValue: "CrmDiscussionNotifyUserIds");
            migrationBuilder.DeleteData(table: "AppSettings", keyColumn: "Key", keyValue: "CrmDriveParentFolderId");
            migrationBuilder.DeleteData(table: "AppSettings", keyColumn: "Key", keyValue: "CrmRelationshipTypes");

            migrationBuilder.UpdateData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "30a1b2c3-d4e5-4678-9abc-def012345801",
                column: "Description",
                value: "CRM-scoped user role (view the pipeline).");

            migrationBuilder.UpdateData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "30a1b2c3-d4e5-4678-9abc-def012345802",
                column: "Description",
                value: "CRM-scoped editor role (create and edit opportunities and activities).");

            migrationBuilder.UpdateData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "30a1b2c3-d4e5-4678-9abc-def012345803",
                column: "Description",
                value: "CRM-scoped manager role (archive and delete).");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "30a1b2c3-d4e5-4678-9abc-def012345805");
        }
    }
}

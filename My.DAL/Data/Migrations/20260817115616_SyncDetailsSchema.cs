using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncDetailsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Snapshot-only sync: PreviousEndDate / PreviousIsAllDay are already created in
            // AddTrackedTaskCorrectionAudit for greenfield installs. This migration exists so
            // the model snapshot matches ApplicationDbContext (no pending model changes).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

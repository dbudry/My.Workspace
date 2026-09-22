using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <summary>
    /// Busy-only availability: Projects.CountsAsTime. Column is also on Projects in
    /// InitialMigration for greenfield; this only adds when upgrading an older database.
    /// </summary>
    public partial class AddProjectCountsAsTime : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep DF_Projects_CountsAsTime: EF maps HasDefaultValue(true) /
            // ValueGeneratedOnAdd and omits the column on INSERT. Dropping the
            // constraint caused NULL insert failures in integration tests.
            migrationBuilder.Sql("""
                IF COL_LENGTH('Projects', 'CountsAsTime') IS NULL
                BEGIN
                    ALTER TABLE [Projects] ADD [CountsAsTime] bit NOT NULL
                        CONSTRAINT [DF_Projects_CountsAsTime] DEFAULT 1;
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('Projects', 'CountsAsTime') IS NOT NULL
                    ALTER TABLE [Projects] DROP COLUMN [CountsAsTime];
                """);
        }
    }
}

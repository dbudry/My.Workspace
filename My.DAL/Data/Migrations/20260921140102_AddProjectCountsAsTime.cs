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
            migrationBuilder.Sql("""
                IF COL_LENGTH('Projects', 'CountsAsTime') IS NULL
                BEGIN
                    ALTER TABLE [Projects] ADD [CountsAsTime] bit NOT NULL
                        CONSTRAINT [DF_Projects_CountsAsTime] DEFAULT 1;
                END
                """);
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[DF_Projects_CountsAsTime]', N'D') IS NOT NULL
                    ALTER TABLE [Projects] DROP CONSTRAINT [DF_Projects_CountsAsTime];
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

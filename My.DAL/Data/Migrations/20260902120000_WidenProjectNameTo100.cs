using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using My.DAL.Data;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <summary>
    /// Project.Name is nvarchar(100) in InitialMigration for greenfield.
    /// This widens older databases that still have nvarchar(max) or a shorter max.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260902120000_WidenProjectNameTo100")]
    public partial class WidenProjectNameTo100 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('Projects', 'Name') IS NOT NULL
                   AND EXISTS (
                       SELECT 1 FROM sys.columns c
                       INNER JOIN sys.tables t ON c.object_id = t.object_id
                       WHERE t.name = 'Projects' AND c.name = 'Name'
                         AND (c.max_length = -1 OR c.max_length <> 200)
                   )
                BEGIN
                    ALTER TABLE [Projects] ALTER COLUMN [Name] nvarchar(100) NOT NULL;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: do not shrink production names.
        }
    }
}

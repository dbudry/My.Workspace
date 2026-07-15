using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillIdentityUserStamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Imported/legacy users may have null stamps; Identity UserManager.UpdateAsync throws.
            migrationBuilder.Sql("""
                UPDATE AspNetUsers
                SET SecurityStamp = CONVERT(nvarchar(36), NEWID())
                WHERE SecurityStamp IS NULL OR LTRIM(RTRIM(SecurityStamp)) = '';

                UPDATE AspNetUsers
                SET ConcurrencyStamp = CONVERT(nvarchar(36), NEWID())
                WHERE ConcurrencyStamp IS NULL OR LTRIM(RTRIM(ConcurrencyStamp)) = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleChannelToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleChannelToken",
                table: "UserSettings",
                type: "nvarchar(max)",
                nullable: true);

            // Legacy watches registered channelToken = channelId; backfill so validation works until renewal.
            migrationBuilder.Sql("""
                UPDATE UserSettings
                SET GoogleChannelToken = GoogleChannelId
                WHERE GoogleChannelId IS NOT NULL AND GoogleChannelToken IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleChannelToken",
                table: "UserSettings");
        }
    }
}

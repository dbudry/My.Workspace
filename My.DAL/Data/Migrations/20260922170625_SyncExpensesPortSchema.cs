using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <summary>
    /// History-only: aligns the model snapshot after the expenses port.
    /// TrackedTask/Stopwatch free-text is already Details in InitialMigration;
    /// PP designer snapshots still carried Name, which triggered PendingModelChanges.
    /// </summary>
    public partial class SyncExpensesPortSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

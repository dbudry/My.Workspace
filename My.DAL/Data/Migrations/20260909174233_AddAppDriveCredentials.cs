using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <summary>
    /// App Drive credential singleton + ExpenseReceipt DriveFileId.
    /// Tables/columns are also in InitialMigration for greenfield.
    /// </summary>
    public partial class AddAppDriveCredentials : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[AppDriveCredentials]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [AppDriveCredentials] (
                        [AppDriveCredentialId] nvarchar(32) NOT NULL,
                        [EncryptedRefreshToken] nvarchar(max) NULL,
                        [Email] nvarchar(256) NULL,
                        [SharedDriveId] nvarchar(128) NULL,
                        [ConnectedAt] datetime2 NULL,
                        [ConnectedByUserId] nvarchar(450) NULL,
                        CONSTRAINT [PK_AppDriveCredentials] PRIMARY KEY ([AppDriveCredentialId])
                    );
                END
                """);
            migrationBuilder.Sql("""
                IF COL_LENGTH('ExpenseReceipts', 'DriveFileId') IS NULL
                    AND OBJECT_ID(N'[ExpenseReceipts]', N'U') IS NOT NULL
                BEGIN
                    ALTER TABLE [ExpenseReceipts] ADD [DriveFileId] nvarchar(128) NULL;
                END
                """);
            migrationBuilder.Sql("""
                IF COL_LENGTH('ExpenseReceipts', 'Content') IS NOT NULL
                BEGIN
                    ALTER TABLE [ExpenseReceipts] ALTER COLUMN [Content] varbinary(max) NULL;
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[AppDriveCredentials]', N'U') IS NOT NULL
                    DROP TABLE [AppDriveCredentials];
                """);
            migrationBuilder.Sql("""
                IF COL_LENGTH('ExpenseReceipts', 'DriveFileId') IS NOT NULL
                    ALTER TABLE [ExpenseReceipts] DROP COLUMN [DriveFileId];
                """);
        }
    }
}

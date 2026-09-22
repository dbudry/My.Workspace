using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations;

public partial class AddExpenseReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[ExpenseReceipts]', N'U') IS NULL
            BEGIN
                CREATE TABLE [ExpenseReceipts] (
                    [ExpenseReceiptId] nvarchar(450) NOT NULL,
                    [ExpenseLineId] nvarchar(450) NOT NULL,
                    [OriginalFileName] nvarchar(200) NOT NULL,
                    [MimeType] nvarchar(100) NOT NULL,
                    [SizeBytes] int NOT NULL,
                    [Content] varbinary(max) NOT NULL,
                    [UploadedAt] datetime2 NOT NULL,
                    [UploadedByUserId] nvarchar(450) NOT NULL,
                    CONSTRAINT [PK_ExpenseReceipts] PRIMARY KEY ([ExpenseReceiptId]),
                    CONSTRAINT [FK_ExpenseReceipts_ExpenseLines_ExpenseLineId] FOREIGN KEY ([ExpenseLineId]) REFERENCES [ExpenseLines] ([ExpenseLineId]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_ExpenseReceipts_ExpenseLineId] ON [ExpenseReceipts] ([ExpenseLineId]);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[ExpenseReceipts]', N'U') IS NOT NULL DROP TABLE [ExpenseReceipts];
            """);
    }
}

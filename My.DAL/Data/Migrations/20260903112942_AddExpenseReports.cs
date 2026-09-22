using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <summary>
    /// ExpenseReports / ExpenseLines. Also baked into InitialMigration for greenfield.
    /// </summary>
    public partial class AddExpenseReports : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[ExpenseReports]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [ExpenseReports] (
                        [ExpenseReportId] nvarchar(450) NOT NULL,
                        [UserId] nvarchar(450) NOT NULL,
                        [Year] int NOT NULL,
                        [Month] int NOT NULL,
                        [CoverStart] datetime2 NOT NULL,
                        [CoverEnd] datetime2 NOT NULL,
                        [ReportDate] datetime2 NOT NULL,
                        [Status] nvarchar(20) NOT NULL,
                        [SubmittedAt] datetime2 NULL,
                        [SubmittedByUserId] nvarchar(max) NULL,
                        [Purpose] nvarchar(2000) NULL,
                        [PlantOrLocation] nvarchar(100) NULL,
                        [DepartmentId] nvarchar(450) NULL,
                        [ChargeToNote] nvarchar(200) NULL,
                        [EmployeeNameSnapshot] nvarchar(120) NULL,
                        [AddressSnapshot] nvarchar(255) NULL,
                        [MileageRateSnapshot] decimal(18,4) NOT NULL,
                        [DriveUserFolderId] nvarchar(128) NULL,
                        [DrivePeriodFolderId] nvarchar(128) NULL,
                        [CreatedAt] datetime2 NOT NULL,
                        [UpdatedAt] datetime2 NOT NULL,
                        CONSTRAINT [PK_ExpenseReports] PRIMARY KEY ([ExpenseReportId]),
                        CONSTRAINT [FK_ExpenseReports_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_ExpenseReports_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([DepartmentId]) ON DELETE SET NULL
                    );
                    CREATE INDEX [IX_ExpenseReports_DepartmentId] ON [ExpenseReports] ([DepartmentId]);
                    CREATE UNIQUE INDEX [IX_ExpenseReports_UserId_Year_Month] ON [ExpenseReports] ([UserId], [Year], [Month]);
                END
                """);
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[ExpenseLines]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [ExpenseLines] (
                        [ExpenseLineId] nvarchar(450) NOT NULL,
                        [ExpenseReportId] nvarchar(450) NOT NULL,
                        [Date] datetime2 NOT NULL,
                        [Description] nvarchar(200) NOT NULL,
                        [Category] nvarchar(32) NOT NULL,
                        [Amount] decimal(18,2) NOT NULL,
                        [Miles] decimal(18,3) NULL,
                        [TransportationCode] nvarchar(5) NULL,
                        [MiscellaneousCode] nvarchar(5) NULL,
                        [MealBreakfast] bit NOT NULL,
                        [MealLunch] bit NOT NULL,
                        [MealDinner] bit NOT NULL,
                        [SortOrder] int NOT NULL,
                        CONSTRAINT [PK_ExpenseLines] PRIMARY KEY ([ExpenseLineId]),
                        CONSTRAINT [FK_ExpenseLines_ExpenseReports_ExpenseReportId] FOREIGN KEY ([ExpenseReportId]) REFERENCES [ExpenseReports] ([ExpenseReportId]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_ExpenseLines_ExpenseReportId] ON [ExpenseLines] ([ExpenseReportId]);
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[ExpenseLines]', N'U') IS NOT NULL DROP TABLE [ExpenseLines];
                IF OBJECT_ID(N'[ExpenseReports]', N'U') IS NOT NULL DROP TABLE [ExpenseReports];
                """);
        }
    }
}

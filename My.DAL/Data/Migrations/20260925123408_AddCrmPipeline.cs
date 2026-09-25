using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[Opportunities]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Opportunities] (
                        [OpportunityId] nvarchar(450) NOT NULL,
                        [Name] nvarchar(200) NOT NULL,
                        [Stage] nvarchar(20) NOT NULL,
                        [Amount] decimal(18,2) NULL,
                        [ExpectedCloseDate] date NULL,
                        [OwnerUserId] nvarchar(450) NULL,
                        [OrganizationId] nvarchar(450) NULL,
                        [ContactId] nvarchar(450) NULL,
                        [Note] nvarchar(500) NULL,
                        [IsActive] bit NOT NULL,
                        [IsArchived] bit NOT NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        [UpdatedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_Opportunities] PRIMARY KEY ([OpportunityId]),
                        CONSTRAINT [FK_Opportunities_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([OrganizationId]) ON DELETE SET NULL
                    );
                    CREATE INDEX [IX_Opportunities_OrganizationId] ON [Opportunities] ([OrganizationId]);
                    CREATE INDEX [IX_Opportunities_Stage] ON [Opportunities] ([Stage]);
                END
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[CrmActivities]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [CrmActivities] (
                        [CrmActivityId] nvarchar(450) NOT NULL,
                        [OpportunityId] nvarchar(450) NOT NULL,
                        [ActivityType] nvarchar(20) NOT NULL,
                        [Subject] nvarchar(200) NOT NULL,
                        [Body] nvarchar(500) NULL,
                        [DueAt] datetimeoffset NULL,
                        [CompletedAt] datetimeoffset NULL,
                        [OwnerUserId] nvarchar(450) NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_CrmActivities] PRIMARY KEY ([CrmActivityId]),
                        CONSTRAINT [FK_CrmActivities_Opportunities_OpportunityId] FOREIGN KEY ([OpportunityId]) REFERENCES [Opportunities] ([OpportunityId]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_CrmActivities_OpportunityId] ON [CrmActivities] ([OpportunityId]);
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [Id] = N'30a1b2c3-d4e5-4678-9abc-def012345801')
                    INSERT INTO [AspNetRoles] ([Id], [ConcurrencyStamp], [Description], [Name], [NormalizedName])
                    VALUES (N'30a1b2c3-d4e5-4678-9abc-def012345801', N'30a1b2c3-d4e5-4678-9abc-def012345801', N'CRM-scoped user role (view the pipeline).', N'User:Crm', N'USER:CRM');
                IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [Id] = N'30a1b2c3-d4e5-4678-9abc-def012345802')
                    INSERT INTO [AspNetRoles] ([Id], [ConcurrencyStamp], [Description], [Name], [NormalizedName])
                    VALUES (N'30a1b2c3-d4e5-4678-9abc-def012345802', N'30a1b2c3-d4e5-4678-9abc-def012345802', N'CRM-scoped editor role (create and edit opportunities and activities).', N'Editor:Crm', N'EDITOR:CRM');
                IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [Id] = N'30a1b2c3-d4e5-4678-9abc-def012345803')
                    INSERT INTO [AspNetRoles] ([Id], [ConcurrencyStamp], [Description], [Name], [NormalizedName])
                    VALUES (N'30a1b2c3-d4e5-4678-9abc-def012345803', N'30a1b2c3-d4e5-4678-9abc-def012345803', N'CRM-scoped manager role (archive and delete).', N'Manager:Crm', N'MANAGER:CRM');
                IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [Id] = N'30a1b2c3-d4e5-4678-9abc-def012345804')
                    INSERT INTO [AspNetRoles] ([Id], [ConcurrencyStamp], [Description], [Name], [NormalizedName])
                    VALUES (N'30a1b2c3-d4e5-4678-9abc-def012345804', N'30a1b2c3-d4e5-4678-9abc-def012345804', N'CRM User Access — assign CRM roles on Users. Does not operate CRM.', N'UserAccess:Crm', N'USERACCESS:CRM');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[CrmActivities]', N'U') IS NOT NULL DROP TABLE [CrmActivities];
                IF OBJECT_ID(N'[Opportunities]', N'U') IS NOT NULL DROP TABLE [Opportunities];
                DELETE FROM [AspNetRoles] WHERE [Id] IN (
                    N'30a1b2c3-d4e5-4678-9abc-def012345801',
                    N'30a1b2c3-d4e5-4678-9abc-def012345802',
                    N'30a1b2c3-d4e5-4678-9abc-def012345803',
                    N'30a1b2c3-d4e5-4678-9abc-def012345804');
                """);
        }
    }
}

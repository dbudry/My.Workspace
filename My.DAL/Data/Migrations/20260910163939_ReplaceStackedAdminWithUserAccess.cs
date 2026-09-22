using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceStackedAdminWithUserAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Insert by name so local DBs that already created Organizations roles
            // at first-login bootstrap do not collide on NormalizedName.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = N'USERACCESS:TYME')
                INSERT INTO AspNetRoles (Id, ConcurrencyStamp, Description, Name, NormalizedName)
                VALUES (N'20a1b2c3-d4e5-4678-9abc-def012345701', N'20a1b2c3-d4e5-4678-9abc-def012345701',
                    N'Tyme User Access — assign Tyme roles on Users. Does not operate Tyme.', N'UserAccess:Tyme', N'USERACCESS:TYME');
                IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = N'USERACCESS:INTRANET')
                INSERT INTO AspNetRoles (Id, ConcurrencyStamp, Description, Name, NormalizedName)
                VALUES (N'20a1b2c3-d4e5-4678-9abc-def012345702', N'20a1b2c3-d4e5-4678-9abc-def012345702',
                    N'Intranet User Access — assign Intranet roles on Users. Does not operate Intranet.', N'UserAccess:Intranet', N'USERACCESS:INTRANET');
                IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = N'USERACCESS:ORGANIZATIONS')
                INSERT INTO AspNetRoles (Id, ConcurrencyStamp, Description, Name, NormalizedName)
                VALUES (N'20a1b2c3-d4e5-4678-9abc-def012345703', N'20a1b2c3-d4e5-4678-9abc-def012345703',
                    N'Organizations User Access — assign Organizations roles on Users.', N'UserAccess:Organizations', N'USERACCESS:ORGANIZATIONS');
                IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = N'USERACCESS:EXPENSES')
                INSERT INTO AspNetRoles (Id, ConcurrencyStamp, Description, Name, NormalizedName)
                VALUES (N'20a1b2c3-d4e5-4678-9abc-def012345704', N'20a1b2c3-d4e5-4678-9abc-def012345704',
                    N'Expenses User Access — assign Expenses roles on Users. Does not operate Expenses.', N'UserAccess:Expenses', N'USERACCESS:EXPENSES');
                IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = N'NAVIGATION:INTRANET')
                INSERT INTO AspNetRoles (Id, ConcurrencyStamp, Description, Name, NormalizedName)
                VALUES (N'20a1b2c3-d4e5-4678-9abc-def012345705', N'20a1b2c3-d4e5-4678-9abc-def012345705',
                    N'Intranet Navigation — curated sidebar tree.', N'Navigation:Intranet', N'NAVIGATION:INTRANET');
                IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = N'MAINTENANCE:ORGANIZATIONS')
                INSERT INTO AspNetRoles (Id, ConcurrencyStamp, Description, Name, NormalizedName)
                VALUES (N'20a1b2c3-d4e5-4678-9abc-def012345706', N'20a1b2c3-d4e5-4678-9abc-def012345706',
                    N'Organizations Maintenance — archive, delete, set active/inactive.', N'Maintenance:Organizations', N'MAINTENANCE:ORGANIZATIONS');
                IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = N'USER:ORGANIZATIONS')
                INSERT INTO AspNetRoles (Id, ConcurrencyStamp, Description, Name, NormalizedName)
                VALUES (N'20a1b2c3-d4e5-4678-9abc-def012345707', N'20a1b2c3-d4e5-4678-9abc-def012345707',
                    N'Organizations-scoped user role (view).', N'User:Organizations', N'USER:ORGANIZATIONS');
                IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = N'EDITOR:ORGANIZATIONS')
                INSERT INTO AspNetRoles (Id, ConcurrencyStamp, Description, Name, NormalizedName)
                VALUES (N'20a1b2c3-d4e5-4678-9abc-def012345708', N'20a1b2c3-d4e5-4678-9abc-def012345708',
                    N'Organizations-scoped editor role (create/edit organizations and departments).', N'Editor:Organizations', N'EDITOR:ORGANIZATIONS');

                INSERT INTO AspNetUserRoles (UserId, RoleId)
                SELECT ur.UserId, n.Id
                FROM AspNetUserRoles ur
                INNER JOIN AspNetRoles o ON o.Id = ur.RoleId AND o.NormalizedName = N'ADMIN:TYME'
                INNER JOIN AspNetRoles n ON n.NormalizedName IN (N'MANAGER:TYME', N'USERACCESS:TYME')
                WHERE NOT EXISTS (SELECT 1 FROM AspNetUserRoles x WHERE x.UserId = ur.UserId AND x.RoleId = n.Id);

                INSERT INTO AspNetUserRoles (UserId, RoleId)
                SELECT ur.UserId, n.Id
                FROM AspNetUserRoles ur
                INNER JOIN AspNetRoles o ON o.Id = ur.RoleId AND o.NormalizedName = N'ADMIN:INTRANET'
                INNER JOIN AspNetRoles n ON n.NormalizedName IN (N'EDITOR:INTRANET', N'NAVIGATION:INTRANET', N'USERACCESS:INTRANET')
                WHERE NOT EXISTS (SELECT 1 FROM AspNetUserRoles x WHERE x.UserId = ur.UserId AND x.RoleId = n.Id);

                INSERT INTO AspNetUserRoles (UserId, RoleId)
                SELECT ur.UserId, n.Id
                FROM AspNetUserRoles ur
                INNER JOIN AspNetRoles o ON o.Id = ur.RoleId AND o.NormalizedName = N'ADMIN:ORGANIZATIONS'
                INNER JOIN AspNetRoles n ON n.NormalizedName IN (N'EDITOR:ORGANIZATIONS', N'MAINTENANCE:ORGANIZATIONS', N'USERACCESS:ORGANIZATIONS')
                WHERE NOT EXISTS (SELECT 1 FROM AspNetUserRoles x WHERE x.UserId = ur.UserId AND x.RoleId = n.Id);

                INSERT INTO AspNetUserRoles (UserId, RoleId)
                SELECT ur.UserId, n.Id
                FROM AspNetUserRoles ur
                INNER JOIN AspNetRoles o ON o.Id = ur.RoleId AND o.NormalizedName = N'ADMIN:EXPENSES'
                INNER JOIN AspNetRoles n ON n.NormalizedName IN (N'MANAGER:EXPENSES', N'USERACCESS:EXPENSES')
                WHERE NOT EXISTS (SELECT 1 FROM AspNetUserRoles x WHERE x.UserId = ur.UserId AND x.RoleId = n.Id);

                DELETE ur FROM AspNetUserRoles ur
                INNER JOIN AspNetRoles r ON r.Id = ur.RoleId
                WHERE r.NormalizedName IN (N'ADMIN:TYME', N'ADMIN:INTRANET', N'ADMIN:ORGANIZATIONS', N'ADMIN:EXPENSES');

                DELETE FROM AspNetRoles
                WHERE NormalizedName IN (N'ADMIN:TYME', N'ADMIN:INTRANET', N'ADMIN:ORGANIZATIONS', N'ADMIN:EXPENSES');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "20a1b2c3-d4e5-4678-9abc-def012345701");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "20a1b2c3-d4e5-4678-9abc-def012345702");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "20a1b2c3-d4e5-4678-9abc-def012345703");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "20a1b2c3-d4e5-4678-9abc-def012345704");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "20a1b2c3-d4e5-4678-9abc-def012345705");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "20a1b2c3-d4e5-4678-9abc-def012345706");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "20a1b2c3-d4e5-4678-9abc-def012345707");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "20a1b2c3-d4e5-4678-9abc-def012345708");

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Description", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { "10a1b2c3-d4e5-4678-9abc-def012345603", "10a1b2c3-d4e5-4678-9abc-def012345603", "Expenses-scoped admin role (module admin).", "Admin:Expenses", "ADMIN:EXPENSES" },
                    { "c3d4e5f6-a7b8-9012-c3d4-e5f6a7b89012", "c3d4e5f6-a7b8-9012-c3d4-e5f6a7b89012", "Tyme-scoped admin role.", "Admin:Tyme", "ADMIN:TYME" },
                    { "f6a7b8c9-d0e1-2345-f6a7-b8c9d0e12345", "f6a7b8c9-d0e1-2345-f6a7-b8c9d0e12345", "Intranet-scoped admin role (full control of navigation structure and content).", "Admin:Intranet", "ADMIN:INTRANET" }
                });
        }
    }
}

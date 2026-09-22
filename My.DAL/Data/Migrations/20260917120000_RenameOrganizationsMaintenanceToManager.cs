using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace My.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameOrganizationsMaintenanceToManager : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE AspNetRoles
                SET Name = N'Manager:Organizations',
                    NormalizedName = N'MANAGER:ORGANIZATIONS',
                    Description = N'Organizations-scoped manager role (archive, delete, set active/inactive; includes edit).'
                WHERE NormalizedName = N'MAINTENANCE:ORGANIZATIONS'
                  AND NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = N'MANAGER:ORGANIZATIONS');

                -- If Manager:Organizations already exists, move holders off Maintenance then drop it.
                INSERT INTO AspNetUserRoles (UserId, RoleId)
                SELECT ur.UserId, mgr.Id
                FROM AspNetUserRoles ur
                INNER JOIN AspNetRoles old ON old.Id = ur.RoleId AND old.NormalizedName = N'MAINTENANCE:ORGANIZATIONS'
                INNER JOIN AspNetRoles mgr ON mgr.NormalizedName = N'MANAGER:ORGANIZATIONS'
                WHERE NOT EXISTS (SELECT 1 FROM AspNetUserRoles x WHERE x.UserId = ur.UserId AND x.RoleId = mgr.Id);

                DELETE ur FROM AspNetUserRoles ur
                INNER JOIN AspNetRoles r ON r.Id = ur.RoleId
                WHERE r.NormalizedName = N'MAINTENANCE:ORGANIZATIONS';

                DELETE FROM AspNetRoles
                WHERE NormalizedName = N'MAINTENANCE:ORGANIZATIONS';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE AspNetRoles
                SET Name = N'Maintenance:Organizations',
                    NormalizedName = N'MAINTENANCE:ORGANIZATIONS',
                    Description = N'Organizations Maintenance — archive, delete, set active/inactive.'
                WHERE NormalizedName = N'MANAGER:ORGANIZATIONS';
                """);
        }
    }
}

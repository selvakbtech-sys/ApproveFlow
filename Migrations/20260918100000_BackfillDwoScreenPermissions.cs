using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.MVC.Migrations
{
    /// <summary>
    /// Data-only migration (no schema change). The DWO matrix gained two rows —
    /// "Discussion Point" and "Sets" — that used to be covered by "DWO Register". Copy each
    /// role's DWO Register flags into the new rows so nobody loses access they had before.
    /// Idempotent: rows that already exist are left alone. Hand-written (no Designer) — the
    /// model snapshot is unchanged.
    /// </summary>
    [DbContext(typeof(ApproveFlow.MVC.Data.AppDbContext))]
    [Migration("20260918100000_BackfillDwoScreenPermissions")]
    public partial class BackfillDwoScreenPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO dbo.RolePermissions (RoleId, ScreenName, CanView, CanCreate, CanEdit, CanApprove, CanDelete, UpdatedAt)
SELECT rp.RoleId, s.ScreenName, rp.CanView, rp.CanCreate, rp.CanEdit, 0, rp.CanDelete, SYSDATETIMEOFFSET()
FROM dbo.RolePermissions rp
CROSS JOIN (VALUES (N'Discussion Point'), (N'Sets')) AS s(ScreenName)
WHERE rp.ScreenName = N'DWO Register'
  AND (rp.CanView = 1 OR rp.CanCreate = 1 OR rp.CanEdit = 1 OR rp.CanApprove = 1 OR rp.CanDelete = 1)
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions x WHERE x.RoleId = rp.RoleId AND x.ScreenName = s.ScreenName);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM dbo.RolePermissions WHERE ScreenName IN (N'Discussion Point', N'Sets');");
        }
    }
}

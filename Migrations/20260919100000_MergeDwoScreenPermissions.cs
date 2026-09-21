using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.MVC.Migrations
{
    /// <summary>
    /// Data-only migration (no schema change). The DWO matrix is reduced to two rows:
    /// "DWO Register" → "Discussion Points" (absorbing the Create/Edit/Delete flags of the
    /// retired "Discussion Point" row) and "Sets" → "DWO Sets". The "DWO Reports" row is
    /// retired; any Discussion Points viewer may open Reports. Idempotent. Hand-written
    /// (no Designer) — the model snapshot is unchanged.
    /// </summary>
    [DbContext(typeof(ApproveFlow.MVC.Data.AppDbContext))]
    [Migration("20260919100000_MergeDwoScreenPermissions")]
    public partial class MergeDwoScreenPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- Fold the requestor-side flags back into the register row before renaming it.
UPDATE r SET
    r.CanView   = CASE WHEN r.CanView   = 1 OR p.CanView   = 1 THEN 1 ELSE 0 END,
    r.CanCreate = CASE WHEN r.CanCreate = 1 OR p.CanCreate = 1 THEN 1 ELSE 0 END,
    r.CanEdit   = CASE WHEN r.CanEdit   = 1 OR p.CanEdit   = 1 THEN 1 ELSE 0 END,
    r.CanDelete = CASE WHEN r.CanDelete = 1 OR p.CanDelete = 1 THEN 1 ELSE 0 END,
    r.UpdatedAt = SYSDATETIMEOFFSET()
FROM dbo.RolePermissions r
JOIN dbo.RolePermissions p ON p.RoleId = r.RoleId AND p.ScreenName = N'Discussion Point'
WHERE r.ScreenName = N'DWO Register';

-- Roles that had a Discussion Point row but no DWO Register row keep their rights.
INSERT INTO dbo.RolePermissions (RoleId, ScreenName, CanView, CanCreate, CanEdit, CanApprove, CanDelete, UpdatedAt)
SELECT p.RoleId, N'DWO Register', p.CanView, p.CanCreate, p.CanEdit, 0, p.CanDelete, SYSDATETIMEOFFSET()
FROM dbo.RolePermissions p
WHERE p.ScreenName = N'Discussion Point'
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions x WHERE x.RoleId = p.RoleId AND x.ScreenName = N'DWO Register');

DELETE FROM dbo.RolePermissions WHERE ScreenName IN (N'Discussion Point', N'DWO Reports');

UPDATE dbo.RolePermissions SET ScreenName = N'Discussion Points', UpdatedAt = SYSDATETIMEOFFSET()
WHERE ScreenName = N'DWO Register'
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions x WHERE x.RoleId = RolePermissions.RoleId AND x.ScreenName = N'Discussion Points');

UPDATE dbo.RolePermissions SET ScreenName = N'DWO Sets', UpdatedAt = SYSDATETIMEOFFSET()
WHERE ScreenName = N'Sets'
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions x WHERE x.RoleId = RolePermissions.RoleId AND x.ScreenName = N'DWO Sets');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE dbo.RolePermissions SET ScreenName = N'DWO Register' WHERE ScreenName = N'Discussion Points';
UPDATE dbo.RolePermissions SET ScreenName = N'Sets'         WHERE ScreenName = N'DWO Sets';
");
        }
    }
}

/* ============================================================================
   Backfill the Admin role's permission matrix
   ----------------------------------------------------------------------------
   Symptom
     Roles & Permissions → Edit Role "Admin" shows several screens with every
     checkbox clear (User Mapping, and in builds carrying the DWO module also
     DWO Register / Management Discussion / DWO Reports), even though the role
     is described as "Full system access. Can perform all actions." and the
     signed-in Admin user really can reach those screens.

   Cause
     The matrix is rendered from the RolePermissions rows that exist for the
     role (RolesController.Edit falls back to false for a screen with no row),
     but access itself never consults them for this role: both checks
     short-circuit on the role NAME first —
         BaseCtrl.HasPerm        : UserRole == "Admin" || <claims>
         _Layout.cshtml CanView  : urole    == "Admin" || <claims>
     so an Admin sees every screen whether or not a row is present. The rows
     for those screens were simply never written, and the grid shows that
     honestly while the bypass ignores it.

   What this script does
     Writes the missing rows so the matrix agrees with the access the Admin
     role actually has. It does NOT change the bypass: the application still
     treats the role named "Admin" as unconditionally full-access, exactly as
     before. This is a data backfill only — no schema change, no code change.

   Safe to re-run: every statement is guarded, so a second run is a no-op.

   NOTE  Permission claims are minted at login, so an Admin who is signed in
         while this runs keeps their current claims until they sign out and
         back in. Their access is unaffected either way, because of the bypass.
   ============================================================================ */

SET NOCOUNT ON;

BEGIN TRANSACTION;

-- The role the application grants unconditional access to, keyed on the exact
-- name the two checks above compare against.
DECLARE @AdminRoleName nvarchar(200) = N'Admin';

-- Screens the Roles & Permissions matrix renders, mirroring
-- Models/ViewModels/RoleVMs.cs → RoleFormVM.AllScreens / ReportScreens.
-- A report screen carries View only; Create/Edit/Approve/Delete are forced off
-- for it on save (PermRowVM.IsReport), so it is seeded the same way here.
DECLARE @Screens TABLE (ScreenName nvarchar(200) PRIMARY KEY, IsReport bit NOT NULL);

INSERT INTO @Screens (ScreenName, IsReport) VALUES
    (N'Dashboard',           1),
    (N'Requests',            0),
    (N'New Request',         0),
    (N'User Management',     0),
    (N'User Mapping',        0),
    (N'Roles & Permissions', 0),
    (N'Workflow Definition', 0),
    (N'Locations',           0),
    (N'Departments',         0),
    (N'Request Types',       0);

-- Also cover any screen this deployment already knows about but the list above
-- does not name — the DWO screens are the case that matters, since the full
-- BNR FLOW build carries them and this repository's AllScreens does not. Taking
-- them from the table avoids guessing at names that differ between builds.
INSERT INTO @Screens (ScreenName, IsReport)
SELECT DISTINCT rp.ScreenName, 0
FROM   dbo.RolePermissions AS rp
WHERE  rp.ScreenName NOT IN (SELECT ScreenName FROM @Screens);

-- ── 1. Insert the rows that are missing entirely ────────────────────────────
INSERT INTO dbo.RolePermissions
        (RoleId, ScreenName, CanView, CanCreate, CanEdit, CanApprove, CanDelete, UpdatedAt)
SELECT  r.RoleId,
        s.ScreenName,
        1,                                                  -- CanView
        CASE WHEN s.IsReport = 1 THEN 0 ELSE 1 END,         -- CanCreate
        CASE WHEN s.IsReport = 1 THEN 0 ELSE 1 END,         -- CanEdit
        CASE WHEN s.IsReport = 1 THEN 0 ELSE 1 END,         -- CanApprove
        CASE WHEN s.IsReport = 1 THEN 0 ELSE 1 END,         -- CanDelete
        SYSDATETIMEOFFSET()
FROM    dbo.Roles AS r
CROSS JOIN @Screens AS s
WHERE   r.RoleName = @AdminRoleName
AND     NOT EXISTS (SELECT 1
                    FROM   dbo.RolePermissions AS rp
                    WHERE  rp.RoleId     = r.RoleId
                    AND    rp.ScreenName = s.ScreenName);

PRINT CONCAT(N'Rows inserted: ', @@ROWCOUNT);

-- ── 2. Top up rows that exist but are only partly ticked ────────────────────
-- Without this a screen that already has a row with, say, Delete cleared would
-- still read as less than full access in the matrix.
UPDATE  rp
SET     rp.CanView    = 1,
        rp.CanCreate  = CASE WHEN s.IsReport = 1 THEN 0 ELSE 1 END,
        rp.CanEdit    = CASE WHEN s.IsReport = 1 THEN 0 ELSE 1 END,
        rp.CanApprove = CASE WHEN s.IsReport = 1 THEN 0 ELSE 1 END,
        rp.CanDelete  = CASE WHEN s.IsReport = 1 THEN 0 ELSE 1 END,
        rp.UpdatedAt  = SYSDATETIMEOFFSET()
FROM    dbo.RolePermissions AS rp
JOIN    dbo.Roles           AS r ON r.RoleId     = rp.RoleId
JOIN    @Screens            AS s ON s.ScreenName = rp.ScreenName
WHERE   r.RoleName = @AdminRoleName
AND    (rp.CanView    <> 1
     OR rp.CanCreate  <> CASE WHEN s.IsReport = 1 THEN 0 ELSE 1 END
     OR rp.CanEdit    <> CASE WHEN s.IsReport = 1 THEN 0 ELSE 1 END
     OR rp.CanApprove <> CASE WHEN s.IsReport = 1 THEN 0 ELSE 1 END
     OR rp.CanDelete  <> CASE WHEN s.IsReport = 1 THEN 0 ELSE 1 END);

PRINT CONCAT(N'Rows updated: ', @@ROWCOUNT);

COMMIT TRANSACTION;

-- ── Verification — every screen should now read 1 (Dashboard: View only) ────
SELECT  rp.ScreenName, rp.CanView, rp.CanCreate, rp.CanEdit, rp.CanApprove, rp.CanDelete
FROM    dbo.RolePermissions AS rp
JOIN    dbo.Roles           AS r ON r.RoleId = rp.RoleId
WHERE   r.RoleName = @AdminRoleName
ORDER BY rp.ScreenName;

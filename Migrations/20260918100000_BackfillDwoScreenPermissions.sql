-- ============================================================================
-- BackfillDwoScreenPermissions — data only, no schema change.
--
-- The Roles & Permissions matrix gained two DWO rows, "Discussion Point" and "Sets",
-- that were previously covered by "DWO Register". This copies each role's DWO Register
-- flags (View/Create/Edit/Delete; Approve stays HOD-only on DWO Register) into the two
-- new rows so existing roles keep the access they had, then registers the EF migration.
--
-- Use this on the SERVER database (the dev database is handled by running
-- Update-Database in Visual Studio's Package Manager Console). Idempotent.
-- ============================================================================

INSERT INTO dbo.RolePermissions (RoleId, ScreenName, CanView, CanCreate, CanEdit, CanApprove, CanDelete, UpdatedAt)
SELECT rp.RoleId, s.ScreenName, rp.CanView, rp.CanCreate, rp.CanEdit, 0, rp.CanDelete, SYSDATETIMEOFFSET()
FROM dbo.RolePermissions rp
CROSS JOIN (VALUES (N'Discussion Point'), (N'Sets')) AS s(ScreenName)
WHERE rp.ScreenName = N'DWO Register'
  AND (rp.CanView = 1 OR rp.CanCreate = 1 OR rp.CanEdit = 1 OR rp.CanApprove = 1 OR rp.CanDelete = 1)
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions x WHERE x.RoleId = rp.RoleId AND x.ScreenName = s.ScreenName);
GO

-- Mark the migration as applied so EF's Update-Database skips it on this database.
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260918100000_BackfillDwoScreenPermissions')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260918100000_BackfillDwoScreenPermissions', N'8.0.6');
GO

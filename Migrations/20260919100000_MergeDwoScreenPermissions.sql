-- ============================================================================
-- MergeDwoScreenPermissions — data only, no schema change.
--
-- The DWO rows in Roles & Permissions are reduced to two:
--   "DWO Register" → "Discussion Points" (also absorbs Create/Edit/Delete from the retired
--                    "Discussion Point" row)
--   "Sets"         → "DWO Sets"
-- "DWO Reports" is retired (any Discussion Points viewer may open Reports).
--
-- Use this on the SERVER database (the dev database is handled by running
-- Update-Database in Visual Studio's Package Manager Console). Idempotent.
-- ============================================================================

UPDATE r SET
    r.CanView   = CASE WHEN r.CanView   = 1 OR p.CanView   = 1 THEN 1 ELSE 0 END,
    r.CanCreate = CASE WHEN r.CanCreate = 1 OR p.CanCreate = 1 THEN 1 ELSE 0 END,
    r.CanEdit   = CASE WHEN r.CanEdit   = 1 OR p.CanEdit   = 1 THEN 1 ELSE 0 END,
    r.CanDelete = CASE WHEN r.CanDelete = 1 OR p.CanDelete = 1 THEN 1 ELSE 0 END,
    r.UpdatedAt = SYSDATETIMEOFFSET()
FROM dbo.RolePermissions r
JOIN dbo.RolePermissions p ON p.RoleId = r.RoleId AND p.ScreenName = N'Discussion Point'
WHERE r.ScreenName = N'DWO Register';
GO

INSERT INTO dbo.RolePermissions (RoleId, ScreenName, CanView, CanCreate, CanEdit, CanApprove, CanDelete, UpdatedAt)
SELECT p.RoleId, N'DWO Register', p.CanView, p.CanCreate, p.CanEdit, 0, p.CanDelete, SYSDATETIMEOFFSET()
FROM dbo.RolePermissions p
WHERE p.ScreenName = N'Discussion Point'
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions x WHERE x.RoleId = p.RoleId AND x.ScreenName = N'DWO Register');
GO

DELETE FROM dbo.RolePermissions WHERE ScreenName IN (N'Discussion Point', N'DWO Reports');
GO

UPDATE dbo.RolePermissions SET ScreenName = N'Discussion Points', UpdatedAt = SYSDATETIMEOFFSET()
WHERE ScreenName = N'DWO Register'
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions x WHERE x.RoleId = RolePermissions.RoleId AND x.ScreenName = N'Discussion Points');
GO

UPDATE dbo.RolePermissions SET ScreenName = N'DWO Sets', UpdatedAt = SYSDATETIMEOFFSET()
WHERE ScreenName = N'Sets'
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions x WHERE x.RoleId = RolePermissions.RoleId AND x.ScreenName = N'DWO Sets');
GO

-- Mark the migration as applied so EF's Update-Database skips it on this database.
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260919100000_MergeDwoScreenPermissions')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260919100000_MergeDwoScreenPermissions', N'8.0.6');
GO

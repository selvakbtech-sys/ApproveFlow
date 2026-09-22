-- ============================================================================
-- AddDwoSetTitle - optional working title on a DWO Set (DwoSets.Title nvarchar(200) NULL).
--
-- Use this on the SERVER database (the dev database is handled by running
-- Update-Database in Visual Studio's Package Manager Console). Idempotent.
-- ============================================================================

IF COL_LENGTH('dbo.DwoSets', 'Title') IS NULL
    ALTER TABLE dbo.DwoSets ADD [Title] nvarchar(200) NULL;
GO

-- Mark the migration as applied so EF's Update-Database skips it on this database.
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260918140000_AddDwoSetTitle')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260918140000_AddDwoSetTitle', N'8.0.6');
GO

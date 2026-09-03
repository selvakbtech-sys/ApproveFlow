-- ============================================================================
-- AddIdempotencyKey — adds the IdempotencyKey column + filtered unique index to
-- the Requests table and registers the EF Core migration so a later
-- Update-Database will not try to reapply it.
--
-- Use this on the SERVER database (the dev database is handled by running
-- Update-Database in Visual Studio's Package Manager Console). Idempotent: each
-- step is guarded, so running it more than once is safe.
--
-- The column is NULLABLE and the index is FILTERED to non-NULL values, so every
-- existing row is left untouched and no back-fill is needed.
--
-- Run against the ApproveFlow database.
-- ============================================================================

-- A FILTERED index (WHERE ...) requires QUOTED_IDENTIFIER ON at creation time.
-- SSMS sets it by default; sqlcmd does NOT, so set it explicitly here rather than
-- relying on the caller passing -I.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.Requests', 'IdempotencyKey') IS NULL
    ALTER TABLE dbo.Requests ADD IdempotencyKey nvarchar(64) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_Requests_IdempotencyKey'
                 AND object_id = OBJECT_ID('dbo.Requests'))
    CREATE UNIQUE INDEX IX_Requests_IdempotencyKey
        ON dbo.Requests (IdempotencyKey)
        WHERE IdempotencyKey IS NOT NULL;
GO

-- Mark the migration as applied so EF's Update-Database skips it on this database.
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260903090000_AddIdempotencyKey')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260903090000_AddIdempotencyKey', N'8.0.6');
GO

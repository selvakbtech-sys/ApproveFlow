-- ============================================================================
-- AddRevokeFields — adds the Revoke columns to the Requests table and registers
-- the EF Core migration so a later Update-Database will not try to reapply it.
--
-- Use this on the SERVER database (the dev database is handled by running
-- Update-Database in Visual Studio's Package Manager Console). Idempotent: each
-- step is guarded, so running it more than once is safe.
--
-- Run against the ApproveFlow database.
-- ============================================================================

IF COL_LENGTH('dbo.Requests', 'IsRevoked') IS NULL
    ALTER TABLE dbo.Requests
        ADD IsRevoked bit NOT NULL CONSTRAINT DF_Requests_IsRevoked DEFAULT(0);
GO

IF COL_LENGTH('dbo.Requests', 'RevokedByUserId') IS NULL
    ALTER TABLE dbo.Requests ADD RevokedByUserId int NULL;
GO

IF COL_LENGTH('dbo.Requests', 'RevokedDate') IS NULL
    ALTER TABLE dbo.Requests ADD RevokedDate datetimeoffset NULL;
GO

IF COL_LENGTH('dbo.Requests', 'RevokedRemark') IS NULL
    ALTER TABLE dbo.Requests ADD RevokedRemark nvarchar(500) NULL;
GO

-- Mark the migration as applied so EF's Update-Database skips it on this database.
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260718111655_AddRevokeFields')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260718111655_AddRevokeFields', N'8.0.6');
GO

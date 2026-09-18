-- ============================================================================
-- AddDwoSetDraft - Draft Sets in the DWO module.
--   * DwoSets.Status nvarchar(20) NOT NULL: 'Draft' | 'Submitted' (existing rows = Submitted)
--   * DwoSets.RequestedAppointmentDate becomes NULLable (null only while a Set is a Draft)
--
-- Use this on the SERVER database (the dev database is handled by running
-- Update-Database in Visual Studio's Package Manager Console). Idempotent.
-- ============================================================================

IF COL_LENGTH('dbo.DwoSets', 'Status') IS NULL
    ALTER TABLE dbo.DwoSets
        ADD [Status] nvarchar(20) NOT NULL CONSTRAINT DF_DwoSets_Status DEFAULT(N'Submitted');
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.DwoSets') AND name = 'RequestedAppointmentDate' AND is_nullable = 0)
    ALTER TABLE dbo.DwoSets ALTER COLUMN RequestedAppointmentDate date NULL;
GO

-- Mark the migration as applied so EF's Update-Database skips it on this database.
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260918120000_AddDwoSetDraft')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260918120000_AddDwoSetDraft', N'8.0.6');
GO

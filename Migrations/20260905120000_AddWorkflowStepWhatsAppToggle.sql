-- ============================================================================
-- AddWorkflowStepWhatsAppToggle — adds the per-approval-level
-- "Send WhatsApp to Approver" switch to WorkflowSteps and registers the EF Core
-- migration so a later Update-Database will not try to reapply it.
--
-- Use this on the SERVER database (the dev database is handled by running
-- Update-Database in Visual Studio's Package Manager Console). Idempotent: each
-- step is guarded, so running it more than once is safe.
--
-- The column is NOT NULL with DEFAULT 1, so every existing workflow level
-- back-fills to "enabled" and current behaviour is preserved exactly.
--
-- Run against the ApproveFlow database.
-- ============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.WorkflowSteps', 'IsWhatsAppEnabled') IS NULL
    ALTER TABLE dbo.WorkflowSteps
        ADD IsWhatsAppEnabled bit NOT NULL
        CONSTRAINT DF_WorkflowSteps_IsWhatsAppEnabled DEFAULT (1);
GO

-- Mark the migration as applied so EF's Update-Database skips it on this database.
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260905120000_AddWorkflowStepWhatsAppToggle')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260905120000_AddWorkflowStepWhatsAppToggle', N'8.0.6');
GO

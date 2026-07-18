/* ===========================================================================
   ApproveFlow - deploy script for 2026-07-17 (WhatsApp Approve session fields)
   EF migration equivalent: 20260717052109_AddWhatsAppApproveSessionFields

   TARGET: the SERVER's ApproveFlow database (separate from the Development DB).
   Run BEFORE starting the new build, or the first WhatsApp approval will throw
   on the missing columns.

   Every step is guarded - safe to run more than once.

   VERIFIED 2026-07-17: the server's __EFMigrationsHistory ends at
   20260707101035_AddApprovalRepliesTable, i.e. it is behind by exactly this one
   migration and has no migrations Development lacks. This script is therefore
   sufficient on its own - no catch-up scripting needed.
   =========================================================================== */

SET XACT_ABORT ON;
GO

/* -- 1. Columns ---------------------------------------------------------- */

IF COL_LENGTH('dbo.WhatsAppPendingClarifications', 'WhatsAppMessageId') IS NULL
    ALTER TABLE dbo.WhatsAppPendingClarifications ADD [WhatsAppMessageId] nvarchar(200) NULL;
GO
IF COL_LENGTH('dbo.WhatsAppPendingClarifications', 'ParentMessageId') IS NULL
    ALTER TABLE dbo.WhatsAppPendingClarifications ADD [ParentMessageId] nvarchar(200) NULL;
GO
IF COL_LENGTH('dbo.WhatsAppPendingClarifications', 'ApprovalRemark') IS NULL
    ALTER TABLE dbo.WhatsAppPendingClarifications ADD [ApprovalRemark] nvarchar(max) NULL;
GO
IF COL_LENGTH('dbo.WhatsAppPendingClarifications', 'ExpiresOn') IS NULL
    ALTER TABLE dbo.WhatsAppPendingClarifications ADD [ExpiresOn] datetimeoffset NULL;
GO
IF COL_LENGTH('dbo.WhatsAppPendingClarifications', 'CompletedOn') IS NULL
    ALTER TABLE dbo.WhatsAppPendingClarifications ADD [CompletedOn] datetimeoffset NULL;
GO
/* Matches EF exactly: NOT NULL with a '' default, so existing rows get ''. */
IF COL_LENGTH('dbo.WhatsAppPendingClarifications', 'Status') IS NULL
    ALTER TABLE dbo.WhatsAppPendingClarifications ADD [Status] nvarchar(20) NOT NULL DEFAULT N'';
GO

/* -- 2. Backfill rows that existed before the migration -------------------
   EF's ALTER leaves pre-existing rows at Status = '', which the app treats as
   neither open nor closed - they would simply stop working. Fix both cases:

   - Reject / Clarification pendings are still waiting for the approver to type
     a reason -> WaitingForReply, so they keep working exactly as before.
   - Approve rows predate this build and are stale. Mark them Expired, NOT
     WaitingForReply: the new timeout sweep would otherwise treat them as due
     and silently auto-approve old requests the moment the server starts.
*/

UPDATE dbo.WhatsAppPendingClarifications
   SET [Status] = N'WaitingForReply'
 WHERE ISNULL([Status], N'') = N''
   AND [Action] <> N'approved';
GO

UPDATE dbo.WhatsAppPendingClarifications
   SET [Status] = N'Expired'
 WHERE ISNULL([Status], N'') = N''
   AND [Action] = N'approved';
GO

/* -- 3. Indexes ----------------------------------------------------------
   The sweep polls every few seconds and every inbound reply resolves by
   message id; completed sessions are retained, so the table only grows.
*/

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_WhatsAppPendingClarifications_Action_Status_ExpiresOn'
                 AND object_id = OBJECT_ID('dbo.WhatsAppPendingClarifications'))
    CREATE INDEX [IX_WhatsAppPendingClarifications_Action_Status_ExpiresOn]
        ON dbo.WhatsAppPendingClarifications ([Action], [Status], [ExpiresOn]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_WhatsAppPendingClarifications_WhatsAppMessageId'
                 AND object_id = OBJECT_ID('dbo.WhatsAppPendingClarifications'))
    CREATE INDEX [IX_WhatsAppPendingClarifications_WhatsAppMessageId]
        ON dbo.WhatsAppPendingClarifications ([WhatsAppMessageId]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_WhatsAppPendingClarifications_ParentMessageId'
                 AND object_id = OBJECT_ID('dbo.WhatsAppPendingClarifications'))
    CREATE INDEX [IX_WhatsAppPendingClarifications_ParentMessageId]
        ON dbo.WhatsAppPendingClarifications ([ParentMessageId]);
GO

/* -- 4. Record the migration so EF does not try to re-apply it ------------ */

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory]
               WHERE [MigrationId] = N'20260717052109_AddWhatsAppApproveSessionFields')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260717052109_AddWhatsAppApproveSessionFields', N'8.0.6');
GO

/* -- 5. Verify ----------------------------------------------------------- */

SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
  FROM INFORMATION_SCHEMA.COLUMNS
 WHERE TABLE_NAME = 'WhatsAppPendingClarifications'
   AND COLUMN_NAME IN ('WhatsAppMessageId','ParentMessageId','ApprovalRemark',
                       'Status','ExpiresOn','CompletedOn')
 ORDER BY COLUMN_NAME;   -- expect 6 rows

SELECT name FROM sys.indexes
 WHERE object_id = OBJECT_ID('dbo.WhatsAppPendingClarifications')
   AND name LIKE 'IX_%';  -- expect 3 rows

SELECT MigrationId FROM [__EFMigrationsHistory]
 WHERE MigrationId = N'20260717052109_AddWhatsAppApproveSessionFields'; -- expect 1 row

SELECT [Status], COUNT(*) AS Rows_
  FROM dbo.WhatsAppPendingClarifications
 GROUP BY [Status];       -- expect NO row with an empty Status
GO

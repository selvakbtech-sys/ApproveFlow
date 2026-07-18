/*==============================================================================
  ApproveFlow — REQUEST DATA ONLY Cleanup  (LIVE-SAFE)
  ------------------------------------------------------------------------------
  Clears ONLY request transactional data: requests, approval chain, history,
  documents, attachments, notifications and WhatsApp staging/history.

  *** TAKE A FULL DATABASE BACKUP BEFORE RUNNING THIS ON LIVE. ***
  This deletes data permanently. It runs in a single transaction and rolls back
  on any error, but a committed run CANNOT be undone without a backup.

  PRESERVED — NOT TOUCHED:
      Users, UserLocations, UserDepartments, UserRequestTypeMappings (user mapping),
      Roles, RolePermissions,
      Locations, Departments, RequestTypes (masters),
      WorkflowDefinitions, WorkflowSteps, WorkflowNotificationRoles (flow)

  DELETED (request-related only):
      Requests, RequestApprovers, RequestActionLog, RequestDocuments,
      ApprovalAttachments, ApprovalReplies, RequestViewTokens, Notifications,
      WhatsAppPendingClarifications, WhatsAppMessageHistory

  Notes:
   - DELETE (not TRUNCATE) is required: these tables are referenced by foreign
     keys, which TRUNCATE does not allow.
   - Delete order is child-first. Notifications -> Requests is NO ACTION (not
     cascade), so Notifications MUST be cleared before Requests.
   - Table names are the EXACT SQL names from the EF DbContext, including the
     SINGULAR RequestActionLog and WhatsAppMessageHistory.
   - Identities are reseeded to 0, so the next request starts at REQ-yy-000001.
==============================================================================*/

SET NOCOUNT ON;
SET XACT_ABORT ON;   -- any runtime error aborts the batch and triggers rollback

BEGIN TRY
    BEGIN TRANSACTION;

    /*--------------------------------------------------------------------------
      1) Request child tables (all cascade from Requests; cleared explicitly).
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.RequestViewTokens;              -- FK -> Requests (Cascade)
    DELETE FROM dbo.ApprovalReplies;                -- FK -> Requests (Cascade); also refs RequestApprovers
    DELETE FROM dbo.ApprovalAttachments;            -- FK -> Requests (Cascade), Users (Restrict)
    DELETE FROM dbo.RequestDocuments;               -- FK -> Requests (Cascade), Users (Restrict)
    DELETE FROM dbo.RequestActionLog;               -- SINGULAR table name
    DELETE FROM dbo.RequestApprovers;               -- FK -> Requests (Cascade), Users (Restrict)

    /*--------------------------------------------------------------------------
      2) Notifications — FK to Requests is NO ACTION, so this MUST run before
         deleting Requests or the Requests delete fails.
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.Notifications;

    /*--------------------------------------------------------------------------
      3) WhatsApp staging + history (RequestId is a plain int, no FK).
         Clearing the pending table also drops any in-flight deferred approvals.
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.WhatsAppPendingClarifications;
    DELETE FROM dbo.WhatsAppMessageHistory;         -- SINGULAR table name

    /*--------------------------------------------------------------------------
      4) Requests last — every child row is gone by now.
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.Requests;

    /*--------------------------------------------------------------------------
      5) Reseed identities so numbering restarts at 1 (REQ-yy-000001).
    --------------------------------------------------------------------------*/
    DBCC CHECKIDENT ('dbo.RequestViewTokens',            RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.ApprovalReplies',              RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.ApprovalAttachments',          RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.RequestDocuments',             RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.RequestActionLog',             RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.RequestApprovers',             RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.Notifications',                RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.WhatsAppPendingClarifications', RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.WhatsAppMessageHistory',       RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.Requests',                     RESEED, 0) WITH NO_INFOMSGS;

    COMMIT TRANSACTION;
    PRINT 'Request data cleared. Users, user mapping, masters, workflows and roles were NOT touched.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    DECLARE @ErrMsg  NVARCHAR(2048) = ERROR_MESSAGE();
    DECLARE @ErrLine INT            = ERROR_LINE();
    PRINT 'Cleanup FAILED and was rolled back. No changes were made.';
    RAISERROR ('Error at line %d: %s', 16, 1, @ErrLine, @ErrMsg);
END CATCH;
GO

/*------------------------------------------------------------------------------
  Verification (read-only). Cleared tables must be 0; preserved tables must NOT.
------------------------------------------------------------------------------*/
SELECT 'CLEARED' AS Bucket, 'Requests'                     AS TableName, COUNT(*) AS [Rows] FROM dbo.Requests
UNION ALL SELECT 'CLEARED', 'RequestApprovers',            COUNT(*) FROM dbo.RequestApprovers
UNION ALL SELECT 'CLEARED', 'RequestActionLog',            COUNT(*) FROM dbo.RequestActionLog
UNION ALL SELECT 'CLEARED', 'RequestDocuments',            COUNT(*) FROM dbo.RequestDocuments
UNION ALL SELECT 'CLEARED', 'ApprovalAttachments',         COUNT(*) FROM dbo.ApprovalAttachments
UNION ALL SELECT 'CLEARED', 'ApprovalReplies',             COUNT(*) FROM dbo.ApprovalReplies
UNION ALL SELECT 'CLEARED', 'RequestViewTokens',           COUNT(*) FROM dbo.RequestViewTokens
UNION ALL SELECT 'CLEARED', 'Notifications',               COUNT(*) FROM dbo.Notifications
UNION ALL SELECT 'CLEARED', 'WhatsAppPendingClarifications', COUNT(*) FROM dbo.WhatsAppPendingClarifications
UNION ALL SELECT 'CLEARED', 'WhatsAppMessageHistory',      COUNT(*) FROM dbo.WhatsAppMessageHistory
UNION ALL SELECT 'KEPT',    'Users',                       COUNT(*) FROM dbo.Users
UNION ALL SELECT 'KEPT',    'UserLocations',               COUNT(*) FROM dbo.UserLocations
UNION ALL SELECT 'KEPT',    'UserDepartments',             COUNT(*) FROM dbo.UserDepartments
UNION ALL SELECT 'KEPT',    'UserRequestTypeMappings',     COUNT(*) FROM dbo.UserRequestTypeMappings
UNION ALL SELECT 'KEPT',    'Roles',                       COUNT(*) FROM dbo.Roles
UNION ALL SELECT 'KEPT',    'RolePermissions',             COUNT(*) FROM dbo.RolePermissions
UNION ALL SELECT 'KEPT',    'Locations',                   COUNT(*) FROM dbo.Locations
UNION ALL SELECT 'KEPT',    'Departments',                 COUNT(*) FROM dbo.Departments
UNION ALL SELECT 'KEPT',    'RequestTypes',                COUNT(*) FROM dbo.RequestTypes
UNION ALL SELECT 'KEPT',    'WorkflowDefinitions',         COUNT(*) FROM dbo.WorkflowDefinitions
UNION ALL SELECT 'KEPT',    'WorkflowSteps',               COUNT(*) FROM dbo.WorkflowSteps
UNION ALL SELECT 'KEPT',    'WorkflowNotificationRoles',   COUNT(*) FROM dbo.WorkflowNotificationRoles
ORDER BY Bucket, TableName;

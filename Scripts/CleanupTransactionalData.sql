/*==============================================================================
  ApproveFlow — Transactional Data Cleanup
  ------------------------------------------------------------------------------
  Resets all transactional data (requests, approvals, notifications, WhatsApp
  history, user mappings) and removes every user EXCEPT the Administrator, while
  preserving ALL master configuration.

  Does NOT drop tables, recreate the database, or modify the schema.

  Master data preserved:
      Roles, RolePermissions, Locations, Departments, RequestTypes,
      WorkflowDefinitions, WorkflowSteps, WorkflowNotificationRoles

  Administrator detection:
      The admin account is detected by ROLE (Roles.RoleName = 'Admin'),
      NOT by a hardcoded username. All users in that role are kept; if there is
      no such role or no user in it, the script aborts (fails safe).

  Table names below are the EXACT SQL names from the EF DbContext, including the
  singular RequestActionLog and WhatsAppMessageHistory.
==============================================================================*/

SET NOCOUNT ON;
SET XACT_ABORT ON;   -- any runtime error aborts the batch and triggers rollback

BEGIN TRY
    BEGIN TRANSACTION;

    /*--------------------------------------------------------------------------
      0) Detect the Administrator account by role (no hardcoded username).
    --------------------------------------------------------------------------*/
    DECLARE @AdminRoleId INT =
        (SELECT TOP (1) RoleId FROM dbo.Roles WHERE RoleName = 'Admin');

    IF @AdminRoleId IS NULL
        THROW 50001, 'No role named ''Admin'' was found. Aborting to avoid deleting all users.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE RoleId = @AdminRoleId)
        THROW 50002, 'No user holds the Admin role. Aborting to avoid deleting all users.', 1;

    /*--------------------------------------------------------------------------
      1) Request-dependent child tables.
         All cascade from Requests but also RESTRICT to Users, so they must be
         cleared before both Requests and Users.
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.RequestViewTokens;      -- FK -> Requests (Cascade)
    DELETE FROM dbo.ApprovalAttachments;    -- FK -> Requests (Cascade), Users (Restrict)
    DELETE FROM dbo.RequestDocuments;       -- FK -> Requests (Cascade), Users (Restrict)
    DELETE FROM dbo.RequestActionLog;       -- singular table; FK -> Requests (Cascade), Users (Restrict)
    DELETE FROM dbo.RequestApprovers;       -- FK -> Requests (Cascade), Users (Restrict)

    /*--------------------------------------------------------------------------
      2) Notifications.
         Notifications -> Requests is NO ACTION (not cascade), so this MUST run
         before deleting Requests or the Request delete would fail.
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.Notifications;          -- FK -> Users (Cascade), Requests (NoAction)

    /*--------------------------------------------------------------------------
      3) WhatsApp staging + history (no FK constraints; RequestId is a plain int).
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.WhatsAppPendingClarifications;   -- no FK
    DELETE FROM dbo.WhatsAppMessageHistory;          -- singular table; no FK

    /*--------------------------------------------------------------------------
      4) Requests.
         Now that every child row is gone, Requests can be deleted. This also
         removes the RESTRICT reference Requests -> Users (RequestorId).
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.Requests;               -- FK -> Users (Restrict), master (Restrict), Workflow (SetNull)

    /*--------------------------------------------------------------------------
      5) User mapping tables (cascade from Users, but cleared explicitly).
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.UserLocations;          -- FK -> Users (Cascade), Locations (Restrict)
    DELETE FROM dbo.UserDepartments;        -- FK -> Users (Cascade), Departments (Restrict)
    DELETE FROM dbo.UserRequestTypeMappings;-- FK -> Users (Cascade), RequestTypes (Restrict)

    /*--------------------------------------------------------------------------
      6) Detach master WorkflowSteps from users about to be deleted.
         WorkflowSteps.ApproverUserId -> Users is RESTRICT and the column is
         NULLABLE. We keep the step rows (master data) but null any approver
         reference that points to a non-admin user, otherwise the user delete
         in step 7 would violate the FK.
    --------------------------------------------------------------------------*/
    UPDATE dbo.WorkflowSteps
       SET ApproverUserId = NULL
     WHERE ApproverUserId IS NOT NULL
       AND ApproverUserId NOT IN (SELECT UserId FROM dbo.Users WHERE RoleId = @AdminRoleId);

    /*--------------------------------------------------------------------------
      7) Delete every user except the Administrator(s).
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.Users
     WHERE RoleId <> @AdminRoleId;

    /*--------------------------------------------------------------------------
      8) Reseed identity values.
         Emptied tables restart at 1. Users keeps the admin row(s), so its
         identity is reseeded to the current max (next new user = max + 1).
    --------------------------------------------------------------------------*/
    DBCC CHECKIDENT ('dbo.RequestViewTokens',          RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.ApprovalAttachments',        RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.RequestDocuments',           RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.RequestActionLog',           RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.RequestApprovers',           RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.Notifications',              RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.WhatsAppPendingClarifications', RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.WhatsAppMessageHistory',     RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.Requests',                   RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.UserLocations',              RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.UserDepartments',            RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.UserRequestTypeMappings',    RESEED, 0) WITH NO_INFOMSGS;

    -- Users still holds the admin row(s): reseed to current max so the next
    -- inserted user id continues after the highest surviving id.
    DECLARE @MaxUserId INT = (SELECT ISNULL(MAX(UserId), 0) FROM dbo.Users);
    DECLARE @ReseedUsers NVARCHAR(256) =
        N'DBCC CHECKIDENT (''dbo.Users'', RESEED, ' + CAST(@MaxUserId AS NVARCHAR(12)) + N') WITH NO_INFOMSGS;';
    EXEC (@ReseedUsers);

    COMMIT TRANSACTION;
    PRINT 'Cleanup completed successfully. Master data and Administrator preserved.';
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
  Optional verification (run after the script; read-only).
------------------------------------------------------------------------------*/
-- SELECT 'Users'            AS TableName, COUNT(*) AS Rows FROM dbo.Users
-- UNION ALL SELECT 'Requests',            COUNT(*) FROM dbo.Requests
-- UNION ALL SELECT 'RequestApprovers',    COUNT(*) FROM dbo.RequestApprovers
-- UNION ALL SELECT 'RequestActionLog',    COUNT(*) FROM dbo.RequestActionLog
-- UNION ALL SELECT 'Notifications',       COUNT(*) FROM dbo.Notifications
-- UNION ALL SELECT 'WhatsAppMsgHistory',  COUNT(*) FROM dbo.WhatsAppMessageHistory
-- UNION ALL SELECT 'PendingClarif',       COUNT(*) FROM dbo.WhatsAppPendingClarifications
-- UNION ALL SELECT 'WorkflowDefinitions', COUNT(*) FROM dbo.WorkflowDefinitions
-- UNION ALL SELECT 'WorkflowSteps',       COUNT(*) FROM dbo.WorkflowSteps
-- UNION ALL SELECT 'Roles',               COUNT(*) FROM dbo.Roles;

/*==============================================================================
  ApproveFlow — FULL RESET (transactional data + workflows)
  ------------------------------------------------------------------------------
  Empties every transactional table AND all workflow definitions, removes every
  user except the Administrator, and resets identity values.

  KEEPS (master config, so the app still works / login still valid):
      Roles, RolePermissions, Locations, Departments, RequestTypes
  KEEPS:
      The Administrator user(s) — detected by Roles.RoleName = 'Admin'
      (NOT a hardcoded username).

  Does NOT drop tables, recreate the database, or modify the schema.

  NOTE ON "TRUNCATE": almost every table here is referenced by a FOREIGN KEY, and
  SQL Server forbids TRUNCATE on an FK-referenced table (even if the referencing
  table is empty). This script therefore uses DELETE in dependency order plus
  DBCC CHECKIDENT ... RESEED, which yields the identical end state: empty tables
  with identity counters reset. (For a literal TRUNCATE you would have to drop and
  recreate every foreign key.)

  Table names are the EXACT SQL names from the EF DbContext, including the
  singular RequestActionLog and WhatsAppMessageHistory.
==============================================================================*/

SET NOCOUNT ON;
SET XACT_ABORT ON;   -- any runtime error aborts the batch and forces rollback

BEGIN TRY
    BEGIN TRANSACTION;

    /*--------------------------------------------------------------------------
      0) Detect the Administrator by ROLE (no hardcoded username). Fail safe if
         there is no Admin role or no user in it.
    --------------------------------------------------------------------------*/
    DECLARE @AdminRoleId INT =
        (SELECT TOP (1) RoleId FROM dbo.Roles WHERE RoleName = 'Admin');

    IF @AdminRoleId IS NULL
        THROW 50001, 'No role named ''Admin'' was found. Aborting to avoid deleting all users.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE RoleId = @AdminRoleId)
        THROW 50002, 'No user holds the Admin role. Aborting to avoid deleting all users.', 1;

    /*--------------------------------------------------------------------------
      1) Request-dependent child tables (RESTRICT to Users, Cascade from Requests).
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.RequestViewTokens;      -- FK -> Requests (Cascade)
    DELETE FROM dbo.ApprovalAttachments;    -- FK -> Requests (Cascade), Users (Restrict)
    DELETE FROM dbo.RequestDocuments;       -- FK -> Requests (Cascade), Users (Restrict)
    DELETE FROM dbo.RequestActionLog;       -- singular table; FK -> Requests (Cascade), Users (Restrict)
    DELETE FROM dbo.RequestApprovers;       -- FK -> Requests (Cascade), Users (Restrict)

    /*--------------------------------------------------------------------------
      2) Notifications -> Requests is NO ACTION, so this MUST precede Requests.
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.Notifications;          -- FK -> Users (Cascade), Requests (NoAction)

    /*--------------------------------------------------------------------------
      3) WhatsApp staging + history (no FK constraints).
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.WhatsAppPendingClarifications;   -- no FK
    DELETE FROM dbo.WhatsAppMessageHistory;          -- singular table; no FK

    /*--------------------------------------------------------------------------
      4) Requests (frees the RESTRICT reference Requests -> Users(RequestorId)).
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.Requests;               -- FK -> Users (Restrict), master (Restrict), Workflow (SetNull)

    /*--------------------------------------------------------------------------
      5) User mapping tables.
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.UserLocations;          -- FK -> Users (Cascade), Locations (Restrict)
    DELETE FROM dbo.UserDepartments;        -- FK -> Users (Cascade), Departments (Restrict)
    DELETE FROM dbo.UserRequestTypeMappings;-- FK -> Users (Cascade), RequestTypes (Restrict)

    /*--------------------------------------------------------------------------
      6) Workflows (child -> parent). Deleting WorkflowSteps here also removes the
         RESTRICT reference WorkflowSteps -> Users(ApproverUserId), so users can be
         deleted in step 7 without nulling anything.
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.WorkflowNotificationRoles;  -- FK -> WorkflowDefinitions (Cascade), Roles (Restrict)
    DELETE FROM dbo.WorkflowSteps;              -- FK -> WorkflowDefinitions (Cascade), Users (Restrict)
    DELETE FROM dbo.WorkflowDefinitions;        -- parent; master (Restrict) refs preserved

    /*--------------------------------------------------------------------------
      7) Delete every user except the Administrator(s).
    --------------------------------------------------------------------------*/
    DELETE FROM dbo.Users
     WHERE RoleId <> @AdminRoleId;

    /*--------------------------------------------------------------------------
      8) Reseed identity values. Emptied tables restart at 1; Users keeps the
         admin row(s) so it is reseeded to the current max (next user = max + 1).
    --------------------------------------------------------------------------*/
    DBCC CHECKIDENT ('dbo.RequestViewTokens',             RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.ApprovalAttachments',           RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.RequestDocuments',              RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.RequestActionLog',              RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.RequestApprovers',              RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.Notifications',                 RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.WhatsAppPendingClarifications', RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.WhatsAppMessageHistory',        RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.Requests',                      RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.UserLocations',                 RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.UserDepartments',               RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.UserRequestTypeMappings',       RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.WorkflowNotificationRoles',     RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.WorkflowSteps',                 RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('dbo.WorkflowDefinitions',           RESEED, 0) WITH NO_INFOMSGS;

    DECLARE @MaxUserId INT = (SELECT ISNULL(MAX(UserId), 0) FROM dbo.Users);
    DECLARE @ReseedUsers NVARCHAR(256) =
        N'DBCC CHECKIDENT (''dbo.Users'', RESEED, ' + CAST(@MaxUserId AS NVARCHAR(12)) + N') WITH NO_INFOMSGS;';
    EXEC (@ReseedUsers);

    COMMIT TRANSACTION;
    PRINT 'FULL RESET completed. Transactional data + workflows cleared; Admin and master config preserved.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    DECLARE @ErrMsg  NVARCHAR(2048) = ERROR_MESSAGE();
    DECLARE @ErrLine INT            = ERROR_LINE();
    PRINT 'FULL RESET FAILED and was rolled back. No changes were made.';
    RAISERROR ('Error at line %d: %s', 16, 1, @ErrLine, @ErrMsg);
END CATCH;
GO

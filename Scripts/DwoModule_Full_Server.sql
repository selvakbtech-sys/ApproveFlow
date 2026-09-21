-- ============================================================================
-- DwoModule_Full_Server — COMPLETE DWO (Discussion Work Order) module for the
-- SERVER database (BNRSVR02 / ApproveFlow). Generated 2026-09-19.
--
-- One script, run end to end in SSMS against the ApproveFlow database. It applies,
-- in order, everything the dev database received through EF migrations:
--
--   1. 20260909055145_AddDwoModule            Company master, DwoSets, DwoPoints,
--                                              DwoAttachments, DwoReferenceLinks,
--                                              DwoFollowUps, DwoHistory, permission rows
--   2. 20260918100000_BackfillDwoScreenPermissions  (data only; no-op on a fresh install)
--   3. 20260918120000_AddDwoSetDraft           DwoSets.Status (Draft | Submitted)
--   4. 20260918140000_AddDwoSetTitle           DwoSets.Title
--   5. 20260919100000_MergeDwoScreenPermissions  matrix rows -> "Discussion Points", "DWO Sets"
--   6. 20260919120000_AddDwoRemarks            DwoRemarks table, DwoHistory.RoleKind/Changes,
--                                              DwoPoints.HeldFromStatus
--
-- Every step is guarded (IF NOT EXISTS / COL_LENGTH / OBJECT_ID), so the script is
-- IDEMPOTENT: safe to run on a server that already has part of the module, and safe to
-- re-run. Each section registers its migration in __EFMigrationsHistory so a later
-- Update-Database on the server skips it.
--
-- Entirely additive to the existing Request / Approval / Workflow / WhatsApp tables.
--
-- After running: grant the DWO rights in Roles & Permissions —
--   "Discussion Points"  View/Create/Edit/Delete = requester screens; Approve = HOD
--   "DWO Sets"           View/Edit = Sets screens
--   "Companies"          Company master
-- Management is derived from the top approval level of active Workflow Definitions.
-- Users must sign out and back in to pick up new permission rows.
-- ============================================================================



-- ############################################################################
-- Scripts/AddDwoModule.sql
-- ############################################################################

-- ============================================================================
-- AddDwoModule — creates the DWO (Discussion Work Order) module tables and the
-- Company master, and registers the four DWO permission screens.
--
-- ENTIRELY ADDITIVE. No existing table, column, index or constraint is altered or
-- dropped. Nothing in the Request / Approval / Workflow / WhatsApp flow reads or writes
-- any of these tables.
--
-- HOW TO USE
--   Dev database : run  Add-Migration AddDwoModule  then  Update-Database  in Visual
--                  Studio's Package Manager Console. Do NOT run the dotnet CLI — it
--                  poisons the build output on this machine. This script is not needed
--                  there.
--   Server (BNRSVR02) database : run THIS script end to end. Its last block registers
--                  migration 20260909055145_AddDwoModule in __EFMigrationsHistory, so a
--                  later Update-Database on the server skips it.
--
-- Idempotent: every step is guarded, so running it more than once is safe.
-- Run against the ApproveFlow database.
-- ============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ── Company master ──────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.Companies', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Companies (
        CompanyId    int IDENTITY(1,1)  NOT NULL CONSTRAINT PK_Companies PRIMARY KEY,
        Code         nvarchar(450)      NOT NULL,
        CompanyName  nvarchar(max)      NOT NULL,
        Description  nvarchar(max)      NULL,
        IsActive     bit                NOT NULL CONSTRAINT DF_Companies_IsActive DEFAULT (1),
        CreatedAt    datetimeoffset     NOT NULL,
        UpdatedAt    datetimeoffset     NOT NULL
    );
    CREATE UNIQUE INDEX IX_Companies_Code ON dbo.Companies (Code);
END
GO

-- ── DwoSets — the HOD's bundle submitted to management ──────────────────────
IF OBJECT_ID('dbo.DwoSets', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DwoSets (
        SetId                    int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DwoSets PRIMARY KEY,
        SetNumber                nvarchar(30)      NOT NULL CONSTRAINT DF_DwoSets_SetNumber DEFAULT (''),
        DepartmentId             int               NOT NULL,
        RequestedAppointmentDate date              NOT NULL,
        AppointmentNotes         nvarchar(1000)    NULL,
        AppointmentStatus        nvarchar(30)      NOT NULL CONSTRAINT DF_DwoSets_ApptStatus DEFAULT ('Requested'),
        ScheduledDate            date              NULL,
        SubmittedByUserId        int               NOT NULL,
        SubmittedAt              datetimeoffset    NOT NULL,
        CreatedAt                datetimeoffset    NOT NULL,
        UpdatedAt                datetimeoffset    NOT NULL,
        CONSTRAINT FK_DwoSets_Departments FOREIGN KEY (DepartmentId)      REFERENCES dbo.Departments (DepartmentId),
        CONSTRAINT FK_DwoSets_Users       FOREIGN KEY (SubmittedByUserId) REFERENCES dbo.Users (UserId)
    );
    -- Filtered: the number is stamped from the identity value in a second save, so a row
    -- is briefly blank between the two.
    CREATE UNIQUE INDEX IX_DwoSets_SetNumber ON dbo.DwoSets (SetNumber) WHERE SetNumber <> '';
    CREATE INDEX IX_DwoSets_DepartmentId ON dbo.DwoSets (DepartmentId);
END
GO

-- ── DwoPoints — the discussion points themselves ────────────────────────────
IF OBJECT_ID('dbo.DwoPoints', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DwoPoints (
        PointId          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DwoPoints PRIMARY KEY,
        PointNumber      nvarchar(30)      NOT NULL CONSTRAINT DF_DwoPoints_PointNumber DEFAULT (''),
        TaskId           nvarchar(60)      NULL,
        Title            nvarchar(250)     NOT NULL,
        Description      nvarchar(max)     NOT NULL,
        Category         nvarchar(60)      NOT NULL CONSTRAINT DF_DwoPoints_Category DEFAULT ('General'),
        Priority         nvarchar(20)      NOT NULL CONSTRAINT DF_DwoPoints_Priority DEFAULT ('Medium'),
        CompanyId        int               NOT NULL,
        LocationId       int               NOT NULL,
        DepartmentId     int               NOT NULL,
        RaisedByUserId   int               NULL,
        Status           nvarchar(30)      NOT NULL CONSTRAINT DF_DwoPoints_Status DEFAULT ('Draft'),
        SetId            int               NULL,
        OriginPointId    int               NULL,
        ReviewedByUserId int               NULL,
        ReviewedAt       datetimeoffset    NULL,
        ReviewRemarks    nvarchar(2000)    NULL,
        CreatedAt        datetimeoffset    NOT NULL,
        UpdatedAt        datetimeoffset    NOT NULL,
        CONSTRAINT FK_DwoPoints_Companies   FOREIGN KEY (CompanyId)        REFERENCES dbo.Companies (CompanyId),
        CONSTRAINT FK_DwoPoints_Locations   FOREIGN KEY (LocationId)       REFERENCES dbo.Locations (LocationId),
        CONSTRAINT FK_DwoPoints_Departments FOREIGN KEY (DepartmentId)     REFERENCES dbo.Departments (DepartmentId),
        CONSTRAINT FK_DwoPoints_RaisedBy    FOREIGN KEY (RaisedByUserId)   REFERENCES dbo.Users (UserId),
        CONSTRAINT FK_DwoPoints_ReviewedBy  FOREIGN KEY (ReviewedByUserId) REFERENCES dbo.Users (UserId),
        CONSTRAINT FK_DwoPoints_Sets        FOREIGN KEY (SetId)            REFERENCES dbo.DwoSets (SetId) ON DELETE SET NULL,
        CONSTRAINT FK_DwoPoints_Origin      FOREIGN KEY (OriginPointId)    REFERENCES dbo.DwoPoints (PointId)
    );
    CREATE UNIQUE INDEX IX_DwoPoints_PointNumber ON dbo.DwoPoints (PointNumber) WHERE PointNumber <> '';
    CREATE INDEX IX_DwoPoints_Department_Status ON dbo.DwoPoints (DepartmentId, Status);
    CREATE INDEX IX_DwoPoints_RaisedBy_Status   ON dbo.DwoPoints (RaisedByUserId, Status);
    CREATE INDEX IX_DwoPoints_SetId             ON dbo.DwoPoints (SetId);
END
GO

-- ── DwoAttachments — every file in the module ───────────────────────────────
IF OBJECT_ID('dbo.DwoAttachments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DwoAttachments (
        AttachmentId     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DwoAttachments PRIMARY KEY,
        PointId          int               NULL,
        SetId            int               NULL,
        Kind             nvarchar(40)      NOT NULL CONSTRAINT DF_DwoAttachments_Kind DEFAULT ('Point'),
        FileName         nvarchar(max)     NOT NULL,
        StoredFileName   nvarchar(max)     NOT NULL,
        FileSizeBytes    bigint            NOT NULL,
        ContentType      nvarchar(max)     NULL,
        UploadedAt       datetimeoffset    NOT NULL,
        UploadedByUserId int               NOT NULL,
        -- NoAction on the Set side: a point already cascades from its own parent, and SQL
        -- Server rejects two cascade paths reaching the same table.
        CONSTRAINT FK_DwoAttachments_Points FOREIGN KEY (PointId)          REFERENCES dbo.DwoPoints (PointId) ON DELETE CASCADE,
        CONSTRAINT FK_DwoAttachments_Sets   FOREIGN KEY (SetId)            REFERENCES dbo.DwoSets (SetId),
        CONSTRAINT FK_DwoAttachments_Users  FOREIGN KEY (UploadedByUserId) REFERENCES dbo.Users (UserId)
    );
    CREATE INDEX IX_DwoAttachments_PointId ON dbo.DwoAttachments (PointId);
    CREATE INDEX IX_DwoAttachments_SetId   ON dbo.DwoAttachments (SetId);
END
GO

-- ── DwoReferenceLinks ───────────────────────────────────────────────────────
IF OBJECT_ID('dbo.DwoReferenceLinks', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DwoReferenceLinks (
        LinkId    int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DwoReferenceLinks PRIMARY KEY,
        PointId   int               NOT NULL,
        Url       nvarchar(1000)    NOT NULL,
        CreatedAt datetimeoffset    NOT NULL,
        CONSTRAINT FK_DwoReferenceLinks_Points FOREIGN KEY (PointId) REFERENCES dbo.DwoPoints (PointId) ON DELETE CASCADE
    );
    CREATE INDEX IX_DwoReferenceLinks_PointId ON dbo.DwoReferenceLinks (PointId);
END
GO

-- ── DwoFollowUps ────────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.DwoFollowUps', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DwoFollowUps (
        FollowUpId        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DwoFollowUps PRIMARY KEY,
        PointId           int               NOT NULL,
        OwnerUserId       int               NOT NULL,
        DueDate           date              NOT NULL,
        Notes             nvarchar(2000)    NULL,
        CompletedAt       datetimeoffset    NULL,
        CompletedByUserId int               NULL,
        CompletionNote    nvarchar(2000)    NULL,
        CreatedAt         datetimeoffset    NOT NULL,
        CONSTRAINT FK_DwoFollowUps_Points      FOREIGN KEY (PointId)           REFERENCES dbo.DwoPoints (PointId) ON DELETE CASCADE,
        CONSTRAINT FK_DwoFollowUps_Owner       FOREIGN KEY (OwnerUserId)       REFERENCES dbo.Users (UserId),
        CONSTRAINT FK_DwoFollowUps_CompletedBy FOREIGN KEY (CompletedByUserId) REFERENCES dbo.Users (UserId)
    );
    CREATE INDEX IX_DwoFollowUps_PointId    ON dbo.DwoFollowUps (PointId);
    CREATE INDEX IX_DwoFollowUps_Owner_Due  ON dbo.DwoFollowUps (OwnerUserId, DueDate);
END
GO

-- ── DwoHistory — immutable audit trail ──────────────────────────────────────
IF OBJECT_ID('dbo.DwoHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DwoHistory (
        HistoryId   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DwoHistory PRIMARY KEY,
        PointId     int               NULL,
        SetId       int               NULL,
        ActorUserId int               NULL,
        ActorName   nvarchar(150)     NOT NULL CONSTRAINT DF_DwoHistory_ActorName DEFAULT (''),
        Action      nvarchar(120)     NOT NULL CONSTRAINT DF_DwoHistory_Action DEFAULT (''),
        Note        nvarchar(2000)    NULL,
        CreatedAt   datetimeoffset    NOT NULL,
        CONSTRAINT FK_DwoHistory_Points FOREIGN KEY (PointId)     REFERENCES dbo.DwoPoints (PointId) ON DELETE CASCADE,
        CONSTRAINT FK_DwoHistory_Sets   FOREIGN KEY (SetId)       REFERENCES dbo.DwoSets (SetId),
        CONSTRAINT FK_DwoHistory_Users  FOREIGN KEY (ActorUserId) REFERENCES dbo.Users (UserId)
    );
    CREATE INDEX IX_DwoHistory_PointId ON dbo.DwoHistory (PointId);
    CREATE INDEX IX_DwoHistory_SetId   ON dbo.DwoHistory (SetId);
END
GO

-- ── Permission rows ─────────────────────────────────────────────────────────
-- Adds the three DWO screens to every existing role with all rights OFF, so no role
-- silently gains access. Grant them from Roles & Permissions:
--   "Discussion Points"     View → the DWO dashboard, the points register and Reports
--                           Create/Edit/Delete → raise / edit / withdraw own points
--                           Approve → HOD powers (review, send back, reject, build a Set)
--   "DWO Sets"              View/Edit → the Sets screens
--   "Companies"             the Company master
-- Management is not a matrix row: Admin, or the approver at the highest step level of an
-- active Workflow Definition, is Management. Admin bypasses the matrix entirely.
INSERT INTO dbo.RolePermissions (RoleId, ScreenName, CanView, CanCreate, CanEdit, CanApprove, CanDelete, UpdatedAt)
SELECT r.RoleId, s.ScreenName, 0, 0, 0, 0, 0, SYSDATETIMEOFFSET()
FROM   dbo.Roles r
CROSS JOIN (VALUES
    ('Discussion Points'), ('DWO Sets'), ('Companies')
) AS s(ScreenName)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.RolePermissions rp
    WHERE rp.RoleId = r.RoleId AND rp.ScreenName = s.ScreenName);
GO

-- Retired rows (earlier builds seeded them). Safe to re-run. Existing databases should run
-- Migrations/20260919100000_MergeDwoScreenPermissions.sql first so no rights are lost.
DELETE FROM dbo.RolePermissions WHERE ScreenName IN ('Management Discussion', 'Discussion Point', 'DWO Reports');
GO

-- ── Starter companies ───────────────────────────────────────────────────────
-- Matches the approved prototype. Delete or rename them from the Companies master.
INSERT INTO dbo.Companies (Code, CompanyName, IsActive, CreatedAt, UpdatedAt)
SELECT v.Code, v.Name, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
FROM (VALUES ('BNRI','BNRI'), ('BNRE','BNRE'), ('UPTOWN','UpTown')) AS v(Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Companies c WHERE c.Code = v.Code);
GO

-- ── Register the migration (SERVER ONLY) ────────────────────────────────────
-- Marks the migration as applied so a later Update-Database on the server skips it.
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260909055145_AddDwoModule')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260909055145_AddDwoModule', N'8.0.6');
GO


-- ############################################################################
-- Migrations/20260918100000_BackfillDwoScreenPermissions.sql
-- ############################################################################

-- ============================================================================
-- BackfillDwoScreenPermissions — data only, no schema change.
--
-- The Roles & Permissions matrix gained two DWO rows, "Discussion Point" and "Sets",
-- that were previously covered by "DWO Register". This copies each role's DWO Register
-- flags (View/Create/Edit/Delete; Approve stays HOD-only on DWO Register) into the two
-- new rows so existing roles keep the access they had, then registers the EF migration.
--
-- Use this on the SERVER database (the dev database is handled by running
-- Update-Database in Visual Studio's Package Manager Console). Idempotent.
-- ============================================================================

INSERT INTO dbo.RolePermissions (RoleId, ScreenName, CanView, CanCreate, CanEdit, CanApprove, CanDelete, UpdatedAt)
SELECT rp.RoleId, s.ScreenName, rp.CanView, rp.CanCreate, rp.CanEdit, 0, rp.CanDelete, SYSDATETIMEOFFSET()
FROM dbo.RolePermissions rp
CROSS JOIN (VALUES (N'Discussion Point'), (N'Sets')) AS s(ScreenName)
WHERE rp.ScreenName = N'DWO Register'
  AND (rp.CanView = 1 OR rp.CanCreate = 1 OR rp.CanEdit = 1 OR rp.CanApprove = 1 OR rp.CanDelete = 1)
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions x WHERE x.RoleId = rp.RoleId AND x.ScreenName = s.ScreenName);
GO

-- Mark the migration as applied so EF's Update-Database skips it on this database.
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260918100000_BackfillDwoScreenPermissions')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260918100000_BackfillDwoScreenPermissions', N'8.0.6');
GO


-- ############################################################################
-- Migrations/20260918120000_AddDwoSetDraft.sql
-- ############################################################################

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


-- ############################################################################
-- Migrations/20260918140000_AddDwoSetTitle.sql
-- ############################################################################

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


-- ############################################################################
-- Migrations/20260919100000_MergeDwoScreenPermissions.sql
-- ############################################################################

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


-- ############################################################################
-- Migrations/20260919120000_AddDwoRemarks.sql
-- ############################################################################

-- ============================================================================
-- AddDwoRemarks - record-wise remark history + field-level audit for DWO points.
--   dbo.DwoRemarks               new (append-only Requester / HOD / Management threads)
--   dbo.DwoHistory.RoleKind      nvarchar(20) NULL
--   dbo.DwoHistory.Changes       nvarchar(max) NULL  (old -> new values on an Edit)
--   dbo.DwoPoints.HeldFromStatus nvarchar(30) NULL   (where a held point resumes to)
--
-- Use this on the SERVER database (the dev database is handled by running
-- Update-Database in Visual Studio's Package Manager Console). Idempotent.
-- ============================================================================

IF COL_LENGTH('dbo.DwoPoints', 'HeldFromStatus') IS NULL
    ALTER TABLE dbo.DwoPoints ADD [HeldFromStatus] nvarchar(30) NULL;
GO
IF COL_LENGTH('dbo.DwoHistory', 'RoleKind') IS NULL
    ALTER TABLE dbo.DwoHistory ADD [RoleKind] nvarchar(20) NULL;
GO
IF COL_LENGTH('dbo.DwoHistory', 'Changes') IS NULL
    ALTER TABLE dbo.DwoHistory ADD [Changes] nvarchar(max) NULL;
GO

IF OBJECT_ID('dbo.DwoRemarks', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DwoRemarks (
        RemarkId   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DwoRemarks PRIMARY KEY,
        PointId    int            NOT NULL,
        RoleKind   nvarchar(20)   NOT NULL,
        UserId     int            NULL,
        UserName   nvarchar(150)  NOT NULL,
        [Action]   nvarchar(40)   NOT NULL,
        Remark     nvarchar(2000) NOT NULL,
        CreatedAt  datetimeoffset NOT NULL,
        CONSTRAINT FK_DwoRemarks_DwoPoints_PointId FOREIGN KEY (PointId) REFERENCES dbo.DwoPoints(PointId) ON DELETE CASCADE,
        CONSTRAINT FK_DwoRemarks_Users_UserId      FOREIGN KEY (UserId)  REFERENCES dbo.Users(UserId)
    );
    CREATE INDEX IX_DwoRemarks_PointId ON dbo.DwoRemarks(PointId);
    CREATE INDEX IX_DwoRemarks_UserId  ON dbo.DwoRemarks(UserId);
END
GO

-- Seed the threads from the review remarks already on file.
INSERT INTO dbo.DwoRemarks (PointId, RoleKind, UserId, UserName, [Action], Remark, CreatedAt)
SELECT p.PointId,
       CASE WHEN p.Status IN (N'Change Requested', N'Rejected') THEN N'HOD' ELSE N'Management' END,
       p.ReviewedByUserId, ISNULL(u.FullName, N''),
       CASE p.Status WHEN N'Change Requested' THEN N'Clarification'
                     WHEN N'Rejected'         THEN N'Reject'
                     WHEN N'Hold'             THEN N'Hold'
                     WHEN N'Closed'           THEN N'Close'
                     WHEN N'Follow-up'        THEN N'Follow-up'
                     WHEN N'Reassigned'       THEN N'Reassign'
                     ELSE N'Edit' END,
       p.ReviewRemarks, ISNULL(p.ReviewedAt, p.UpdatedAt)
FROM dbo.DwoPoints p
LEFT JOIN dbo.Users u ON u.UserId = p.ReviewedByUserId
WHERE p.ReviewRemarks IS NOT NULL AND p.ReviewRemarks <> N''
  AND NOT EXISTS (SELECT 1 FROM dbo.DwoRemarks r WHERE r.PointId = p.PointId);
GO
UPDATE dbo.DwoPoints SET HeldFromStatus = N'Management Discussion' WHERE Status = N'Hold' AND HeldFromStatus IS NULL;
GO

-- Mark the migration as applied so EF's Update-Database skips it on this database.
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260919120000_AddDwoRemarks')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260919120000_AddDwoRemarks', N'8.0.6');
GO

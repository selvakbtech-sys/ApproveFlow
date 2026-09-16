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
--   "DWO Register"          View → the DWO dashboard, My Discussions and the register
--                           Approve → HOD powers (review, send back, reject, build a Set)
--   "DWO Reports"           View → the Reports page
--   "Companies"             the Company master
-- Management is not a matrix row: Admin, or the approver at the highest step level of an
-- active Workflow Definition, is Management. Admin bypasses the matrix entirely.
INSERT INTO dbo.RolePermissions (RoleId, ScreenName, CanView, CanCreate, CanEdit, CanApprove, CanDelete, UpdatedAt)
SELECT r.RoleId, s.ScreenName, 0, 0, 0, 0, 0, SYSDATETIMEOFFSET()
FROM   dbo.Roles r
CROSS JOIN (VALUES
    ('DWO Register'), ('DWO Reports'), ('Companies')
) AS s(ScreenName)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.RolePermissions rp
    WHERE rp.RoleId = r.RoleId AND rp.ScreenName = s.ScreenName);
GO

-- The retired "Management Discussion" row (earlier builds seeded it). Safe to re-run.
DELETE FROM dbo.RolePermissions WHERE ScreenName = 'Management Discussion';
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

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

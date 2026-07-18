BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707101035_AddApprovalRepliesTable'
)
BEGIN
    CREATE TABLE [ApprovalReplies] (
        [ApprovalReplyId] int NOT NULL IDENTITY,
        [ApprovalId] int NOT NULL,
        [RequestId] int NOT NULL,
        [ReplyMessage] nvarchar(max) NULL,
        [AttachmentFileName] nvarchar(max) NULL,
        [AttachmentUrl] nvarchar(max) NULL,
        [WhatsAppMessageId] nvarchar(max) NULL,
        [CreatedDate] datetimeoffset NOT NULL,
        [CreatedBy] int NOT NULL,
        CONSTRAINT [PK_ApprovalReplies] PRIMARY KEY ([ApprovalReplyId]),
        CONSTRAINT [FK_ApprovalReplies_Requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [Requests] ([RequestId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707101035_AddApprovalRepliesTable'
)
BEGIN
    CREATE INDEX [IX_ApprovalReplies_ApprovalId] ON [ApprovalReplies] ([ApprovalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707101035_AddApprovalRepliesTable'
)
BEGIN
    CREATE INDEX [IX_ApprovalReplies_RequestId] ON [ApprovalReplies] ([RequestId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707101035_AddApprovalRepliesTable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260707101035_AddApprovalRepliesTable', N'8.0.6');
END;
GO

COMMIT;
GO


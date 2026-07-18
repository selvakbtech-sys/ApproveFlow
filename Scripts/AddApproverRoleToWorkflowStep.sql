BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082246_AddApproverRoleToWorkflowStep'
)
BEGIN
    ALTER TABLE [WorkflowSteps] ADD [ApproverRoleId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082246_AddApproverRoleToWorkflowStep'
)
BEGIN
    CREATE INDEX [IX_WorkflowSteps_ApproverRoleId] ON [WorkflowSteps] ([ApproverRoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082246_AddApproverRoleToWorkflowStep'
)
BEGIN
    ALTER TABLE [WorkflowSteps] ADD CONSTRAINT [FK_WorkflowSteps_Roles_ApproverRoleId] FOREIGN KEY ([ApproverRoleId]) REFERENCES [Roles] ([RoleId]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082246_AddApproverRoleToWorkflowStep'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260707082246_AddApproverRoleToWorkflowStep', N'8.0.6');
END;
GO

COMMIT;
GO


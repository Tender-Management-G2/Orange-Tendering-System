BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115081531_AddTopExecutivePositions'
)
BEGIN
    ALTER TABLE [CommitteeMembers] ADD [ExecutivePosition] nvarchar(30) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115081531_AddTopExecutivePositions'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CommitteeMembers_TenderId_ExecutivePosition] ON [CommitteeMembers] ([TenderId], [ExecutivePosition]) WHERE [ExecutivePosition] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115081531_AddTopExecutivePositions'
)
BEGIN
    EXEC(N'ALTER TABLE [CommitteeMembers] ADD CONSTRAINT [CK_CommitteeMember_ExecutivePosition] CHECK ([ExecutivePosition] IS NULL OR ([CommitteeType] = ''Top'' AND [ExecutivePosition] IN (''CEO'',''CFO'',''CTO'',''COO'')))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115081531_AddTopExecutivePositions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261115081531_AddTopExecutivePositions', N'10.0.14');
END;

COMMIT;
GO


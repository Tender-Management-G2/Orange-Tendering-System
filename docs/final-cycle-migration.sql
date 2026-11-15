BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113110000_RoundControlAndScheduling'
)
BEGIN
    ALTER TABLE [EvaluationSession] ADD [OpenedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113110000_RoundControlAndScheduling'
)
BEGIN
    ALTER TABLE [EvaluationSession] ADD [ZoomMeetingId] nvarchar(40) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113110000_RoundControlAndScheduling'
)
BEGIN
    CREATE TABLE [ExternalSupplierInvitation] (
        [Id] int NOT NULL IDENTITY,
        [TenderRoundId] int NOT NULL,
        [CompanyName] nvarchar(200) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [Phone] nvarchar(40) NULL,
        [Channel] nvarchar(30) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [Attempts] int NOT NULL,
        [LastError] nvarchar(1000) NULL,
        [CreatedByUserId] nvarchar(450) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [SentAt] datetime2 NULL,
        CONSTRAINT [PK_ExternalSupplierInvitation] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ExternalSupplierInvitation_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ExternalSupplierInvitation_TenderRounds_TenderRoundId] FOREIGN KEY ([TenderRoundId]) REFERENCES [TenderRounds] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113110000_RoundControlAndScheduling'
)
BEGIN
    CREATE TABLE [RoundDecision] (
        [Id] int NOT NULL IDENTITY,
        [TenderRoundId] int NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [OfferCount] int NOT NULL,
        [Justification] nvarchar(2000) NOT NULL,
        [RequestedByUserId] nvarchar(450) NOT NULL,
        [RequestedAt] datetime2 NOT NULL,
        [DecidedByUserId] nvarchar(450) NULL,
        [DecidedAt] datetime2 NULL,
        [DecisionComment] nvarchar(2000) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_RoundDecision] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RoundDecision_AspNetUsers_DecidedByUserId] FOREIGN KEY ([DecidedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RoundDecision_AspNetUsers_RequestedByUserId] FOREIGN KEY ([RequestedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RoundDecision_TenderRounds_TenderRoundId] FOREIGN KEY ([TenderRoundId]) REFERENCES [TenderRounds] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113110000_RoundControlAndScheduling'
)
BEGIN
    CREATE INDEX [IX_ExternalSupplierInvitation_CreatedByUserId] ON [ExternalSupplierInvitation] ([CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113110000_RoundControlAndScheduling'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ExternalSupplierInvitation_TenderRoundId_Email] ON [ExternalSupplierInvitation] ([TenderRoundId], [Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113110000_RoundControlAndScheduling'
)
BEGIN
    CREATE INDEX [IX_RoundDecision_DecidedByUserId] ON [RoundDecision] ([DecidedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113110000_RoundControlAndScheduling'
)
BEGIN
    CREATE INDEX [IX_RoundDecision_RequestedByUserId] ON [RoundDecision] ([RequestedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113110000_RoundControlAndScheduling'
)
BEGIN
    CREATE INDEX [IX_RoundDecision_TenderRoundId_Kind] ON [RoundDecision] ([TenderRoundId], [Kind]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113110000_RoundControlAndScheduling'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261113110000_RoundControlAndScheduling', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113111426_FinalizeTenderCycle'
)
BEGIN
    ALTER TABLE [ExternalSupplierInvitation] ADD [SupplierId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113111426_FinalizeTenderCycle'
)
BEGIN
    CREATE INDEX [IX_ExternalSupplierInvitation_SupplierId] ON [ExternalSupplierInvitation] ([SupplierId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113111426_FinalizeTenderCycle'
)
BEGIN
    ALTER TABLE [ExternalSupplierInvitation] ADD CONSTRAINT [FK_ExternalSupplierInvitation_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113111426_FinalizeTenderCycle'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261113111426_FinalizeTenderCycle', N'10.0.14');
END;

COMMIT;
GO

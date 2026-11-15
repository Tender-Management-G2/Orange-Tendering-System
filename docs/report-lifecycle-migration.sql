BEGIN TRANSACTION;
ALTER TABLE [TenderReports] ADD [PdfContent] varbinary(max) NULL;

ALTER TABLE [TenderReports] ADD [PdfSha256] nvarchar(64) NULL;

ALTER TABLE [TenderReports] ADD [RowVersion] rowversion NOT NULL;

ALTER TABLE [TenderReports] ADD [SignaturePageIndex] int NULL;

ALTER TABLE [TenderReports] ADD [SnapshotJson] nvarchar(max) NULL;

ALTER TABLE [TenderReports] ADD [SnapshotSha256] nvarchar(64) NULL;

ALTER TABLE [TenderReports] ADD [SourceSha256] nvarchar(64) NULL;

ALTER TABLE [TenderReports] ADD [SupersededAt] datetime2 NULL;

ALTER TABLE [TenderReports] ADD [TemplateVersion] nvarchar(40) NULL;

CREATE TABLE [ReportRevision] (
    [Id] int NOT NULL IDENTITY,
    [RequestId] uniqueidentifier NOT NULL,
    [TenderReportId] int NOT NULL,
    [SourceStage] nvarchar(30) NOT NULL,
    [SourceSessionState] nvarchar(30) NOT NULL,
    [TargetStage] nvarchar(30) NOT NULL,
    [Correction] nvarchar(30) NOT NULL,
    [ReturningUserId] nvarchar(450) NOT NULL,
    [ResponsibleUserId] nvarchar(450) NOT NULL,
    [Feedback] nvarchar(4000) NOT NULL,
    [RequiredAction] nvarchar(4000) NOT NULL,
    [RequestedAt] datetime2 NOT NULL,
    [Status] nvarchar(30) NOT NULL,
    [PolicyEvidenceJson] nvarchar(max) NULL,
    [Blocker] nvarchar(2000) NULL,
    [ResubmittedReportId] int NULL,
    [ResubmittedAt] datetime2 NULL,
    [CorrectionSummary] nvarchar(max) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ReportRevision] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ReportRevision_AspNetUsers_ResponsibleUserId] FOREIGN KEY ([ResponsibleUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReportRevision_AspNetUsers_ReturningUserId] FOREIGN KEY ([ReturningUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReportRevision_TenderReports_ResubmittedReportId] FOREIGN KEY ([ResubmittedReportId]) REFERENCES [TenderReports] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReportRevision_TenderReports_TenderReportId] FOREIGN KEY ([TenderReportId]) REFERENCES [TenderReports] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ReportSignature] (
    [Id] bigint NOT NULL IDENTITY,
    [TenderReportId] int NOT NULL,
    [UserId] nvarchar(450) NOT NULL,
    [SignerName] nvarchar(150) NOT NULL,
    [Role] nvarchar(100) NOT NULL,
    [Stage] nvarchar(30) NOT NULL,
    [Slot] nvarchar(80) NOT NULL,
    [Decision] nvarchar(20) NOT NULL,
    [SignedAt] datetime2 NOT NULL,
    [DocumentHash] nvarchar(64) NOT NULL,
    [SnapshotHash] nvarchar(64) NOT NULL,
    [ManifestJson] nvarchar(max) NOT NULL,
    [Method] nvarchar(30) NOT NULL,
    [CmsSignature] varbinary(max) NULL,
    [CertificateThumbprint] nvarchar(128) NULL,
    [InvalidatedAt] datetime2 NULL,
    [InvalidationReason] nvarchar(1000) NULL,
    CONSTRAINT [PK_ReportSignature] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ReportSignature_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReportSignature_TenderReports_TenderReportId] FOREIGN KEY ([TenderReportId]) REFERENCES [TenderReports] ([Id]) ON DELETE NO ACTION
);

CREATE UNIQUE INDEX [IX_ReportRevision_RequestId] ON [ReportRevision] ([RequestId]);

CREATE INDEX [IX_ReportRevision_ResponsibleUserId] ON [ReportRevision] ([ResponsibleUserId]);

CREATE INDEX [IX_ReportRevision_ResubmittedReportId] ON [ReportRevision] ([ResubmittedReportId]);

CREATE INDEX [IX_ReportRevision_ReturningUserId] ON [ReportRevision] ([ReturningUserId]);

CREATE INDEX [IX_ReportRevision_TenderReportId_Status] ON [ReportRevision] ([TenderReportId], [Status]);

CREATE UNIQUE INDEX [IX_ReportSignature_TenderReportId_Stage_Slot] ON [ReportSignature] ([TenderReportId], [Stage], [Slot]);

CREATE UNIQUE INDEX [IX_ReportSignature_TenderReportId_Stage_UserId] ON [ReportSignature] ([TenderReportId], [Stage], [UserId]);

CREATE INDEX [IX_ReportSignature_UserId] ON [ReportSignature] ([UserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261113092128_AutomatedReportLifecycle', N'10.0.14');

COMMIT;
GO

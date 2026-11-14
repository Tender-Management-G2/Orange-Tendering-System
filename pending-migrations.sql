BEGIN TRANSACTION;
ALTER TABLE [Tenders] ADD [AwardApprovalEvidenceJson] nvarchar(max) NULL;

ALTER TABLE [Tenders] ADD [AwardedByUserId] nvarchar(450) NULL;

ALTER TABLE [Tenders] ADD [ClosedAt] datetime2 NULL;

ALTER TABLE [Tenders] ADD [Currency] nvarchar(3) NOT NULL DEFAULT N'';

ALTER TABLE [Tenders] ADD [EstimatedValue] decimal(18,2) NOT NULL DEFAULT 0.0;

ALTER TABLE [Tenders] ADD [IsCritical] bit NOT NULL DEFAULT CAST(0 AS bit);

ALTER TABLE [Tenders] ADD [RowVersion] rowversion NOT NULL;

DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[EvaluationCriteria]') AND [c].[name] = N'Weight');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [EvaluationCriteria] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [EvaluationCriteria] ALTER COLUMN [Weight] decimal(12,6) NULL;

DECLARE @var1 nvarchar(max);
SELECT @var1 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CommitteeRounds]') AND [c].[name] = N'TechnicalWeight');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [CommitteeRounds] DROP CONSTRAINT ' + @var1 + ';');
ALTER TABLE [CommitteeRounds] ALTER COLUMN [TechnicalWeight] decimal(12,6) NULL;

DECLARE @var2 nvarchar(max);
SELECT @var2 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CommitteeRounds]') AND [c].[name] = N'FinancialWeight');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [CommitteeRounds] DROP CONSTRAINT ' + @var2 + ';');
ALTER TABLE [CommitteeRounds] ALTER COLUMN [FinancialWeight] decimal(12,6) NULL;

CREATE TABLE [AiAnalysis] (
    [Id] int NOT NULL IDENTITY,
    [TenderRoundId] int NOT NULL,
    [Status] nvarchar(max) NOT NULL,
    [Model] nvarchar(max) NOT NULL,
    [EvidenceJson] nvarchar(max) NOT NULL,
    [Findings] nvarchar(max) NULL,
    [StartedAt] datetime2 NOT NULL,
    [DurationMilliseconds] bigint NOT NULL,
    [ReviewedByUserId] nvarchar(450) NULL,
    [ReviewedAt] datetime2 NULL,
    CONSTRAINT [PK_AiAnalysis] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AiAnalysis_AspNetUsers_ReviewedByUserId] FOREIGN KEY ([ReviewedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AiAnalysis_TenderRounds_TenderRoundId] FOREIGN KEY ([TenderRoundId]) REFERENCES [TenderRounds] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [WeightRound] (
    [Id] int NOT NULL IDENTITY,
    [TenderId] int NOT NULL,
    [Version] int NOT NULL,
    [RequirementsJson] nvarchar(max) NOT NULL,
    [State] nvarchar(30) NOT NULL,
    [FinancialAverage] decimal(12,6) NULL,
    [TechnicalAverage] decimal(12,6) NULL,
    [ApprovedFinancial] decimal(12,6) NULL,
    [ApprovedTechnical] decimal(12,6) NULL,
    [ApprovedByUserId] nvarchar(450) NULL,
    [ApprovedAt] datetime2 NULL,
    [ApprovalPolicyJson] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_WeightRound] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_WeightRound_Approved] CHECK ([State] <> 'Approved' OR ([ApprovedFinancial] IS NOT NULL AND [ApprovedTechnical] IS NOT NULL AND [ApprovedFinancial] + [ApprovedTechnical] = 100 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAt] IS NOT NULL)),
    CONSTRAINT [FK_WeightRound_AspNetUsers_ApprovedByUserId] FOREIGN KEY ([ApprovedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WeightRound_Tenders_TenderId] FOREIGN KEY ([TenderId]) REFERENCES [Tenders] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [EvaluationSession] (
    [Id] int NOT NULL IDENTITY,
    [TenderRoundId] int NOT NULL,
    [WeightRoundId] int NOT NULL,
    [CommitteeRoundId] int NOT NULL,
    [ReportReviewRoundId] int NULL,
    [AiAnalysisId] int NOT NULL,
    [StartsAt] datetime2 NOT NULL,
    [EndsAt] datetime2 NOT NULL,
    [MeetingUrl] nvarchar(1000) NOT NULL,
    [State] nvarchar(30) NOT NULL,
    [ClosedAt] datetime2 NULL,
    [ClosedByUserId] nvarchar(450) NULL,
    [ClosureReason] nvarchar(max) NULL,
    [FinalApprovalPolicyJson] nvarchar(max) NULL,
    [ReportId] int NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_EvaluationSession] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_EvaluationSession_Window] CHECK ([EndsAt] > [StartsAt]),
    CONSTRAINT [FK_EvaluationSession_AiAnalysis_AiAnalysisId] FOREIGN KEY ([AiAnalysisId]) REFERENCES [AiAnalysis] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_EvaluationSession_AspNetUsers_ClosedByUserId] FOREIGN KEY ([ClosedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_EvaluationSession_CommitteeRounds_CommitteeRoundId] FOREIGN KEY ([CommitteeRoundId]) REFERENCES [CommitteeRounds] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_EvaluationSession_CommitteeRounds_ReportReviewRoundId] FOREIGN KEY ([ReportReviewRoundId]) REFERENCES [CommitteeRounds] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_EvaluationSession_TenderReports_ReportId] FOREIGN KEY ([ReportId]) REFERENCES [TenderReports] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_EvaluationSession_TenderRounds_TenderRoundId] FOREIGN KEY ([TenderRoundId]) REFERENCES [TenderRounds] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_EvaluationSession_WeightRound_WeightRoundId] FOREIGN KEY ([WeightRoundId]) REFERENCES [WeightRound] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [WeightProposal] (
    [Id] int NOT NULL IDENTITY,
    [WeightRoundId] int NOT NULL,
    [CommitteeMemberId] int NOT NULL,
    [MemberUserId] nvarchar(450) NOT NULL,
    [Financial] decimal(9,4) NOT NULL,
    [Technical] decimal(9,4) NOT NULL,
    [SubmittedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_WeightProposal] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_WeightProposal_Valid] CHECK ([Financial] >= 0 AND [Financial] <= 100 AND [Technical] >= 0 AND [Technical] <= 100 AND [Financial] + [Technical] = 100),
    CONSTRAINT [FK_WeightProposal_AspNetUsers_MemberUserId] FOREIGN KEY ([MemberUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WeightProposal_CommitteeMembers_CommitteeMemberId] FOREIGN KEY ([CommitteeMemberId]) REFERENCES [CommitteeMembers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WeightProposal_WeightRound_WeightRoundId] FOREIGN KEY ([WeightRoundId]) REFERENCES [WeightRound] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [EvaluationParticipation] (
    [Id] int NOT NULL IDENTITY,
    [EvaluationSessionId] int NOT NULL,
    [CommitteeMemberId] int NOT NULL,
    [SentAt] datetime2 NOT NULL,
    [SubmittedAt] datetime2 NULL,
    CONSTRAINT [PK_EvaluationParticipation] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_EvaluationParticipation_CommitteeMembers_CommitteeMemberId] FOREIGN KEY ([CommitteeMemberId]) REFERENCES [CommitteeMembers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_EvaluationParticipation_EvaluationSession_EvaluationSessionId] FOREIGN KEY ([EvaluationSessionId]) REFERENCES [EvaluationSession] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SessionBid] (
    [Id] int NOT NULL IDENTITY,
    [EvaluationSessionId] int NOT NULL,
    [BidId] int NOT NULL,
    CONSTRAINT [PK_SessionBid] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SessionBid_Bids_BidId] FOREIGN KEY ([BidId]) REFERENCES [Bids] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SessionBid_EvaluationSession_EvaluationSessionId] FOREIGN KEY ([EvaluationSessionId]) REFERENCES [EvaluationSession] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_Tenders_AwardedByUserId] ON [Tenders] ([AwardedByUserId]);

CREATE INDEX [IX_AiAnalysis_ReviewedByUserId] ON [AiAnalysis] ([ReviewedByUserId]);

CREATE INDEX [IX_AiAnalysis_TenderRoundId] ON [AiAnalysis] ([TenderRoundId]);

CREATE INDEX [IX_EvaluationParticipation_CommitteeMemberId] ON [EvaluationParticipation] ([CommitteeMemberId]);

CREATE UNIQUE INDEX [IX_EvaluationParticipation_EvaluationSessionId_CommitteeMemberId] ON [EvaluationParticipation] ([EvaluationSessionId], [CommitteeMemberId]);

CREATE INDEX [IX_EvaluationSession_AiAnalysisId] ON [EvaluationSession] ([AiAnalysisId]);

CREATE INDEX [IX_EvaluationSession_ClosedByUserId] ON [EvaluationSession] ([ClosedByUserId]);

CREATE INDEX [IX_EvaluationSession_CommitteeRoundId] ON [EvaluationSession] ([CommitteeRoundId]);

CREATE INDEX [IX_EvaluationSession_ReportId] ON [EvaluationSession] ([ReportId]);

CREATE INDEX [IX_EvaluationSession_ReportReviewRoundId] ON [EvaluationSession] ([ReportReviewRoundId]);

CREATE UNIQUE INDEX [IX_EvaluationSession_TenderRoundId] ON [EvaluationSession] ([TenderRoundId]);

CREATE INDEX [IX_EvaluationSession_WeightRoundId] ON [EvaluationSession] ([WeightRoundId]);

CREATE INDEX [IX_SessionBid_BidId] ON [SessionBid] ([BidId]);

CREATE UNIQUE INDEX [IX_SessionBid_EvaluationSessionId_BidId] ON [SessionBid] ([EvaluationSessionId], [BidId]);

CREATE INDEX [IX_WeightProposal_CommitteeMemberId] ON [WeightProposal] ([CommitteeMemberId]);

CREATE INDEX [IX_WeightProposal_MemberUserId] ON [WeightProposal] ([MemberUserId]);

CREATE UNIQUE INDEX [IX_WeightProposal_WeightRoundId_CommitteeMemberId] ON [WeightProposal] ([WeightRoundId], [CommitteeMemberId]);

CREATE UNIQUE INDEX [IX_WeightProposal_WeightRoundId_MemberUserId] ON [WeightProposal] ([WeightRoundId], [MemberUserId]);

CREATE INDEX [IX_WeightRound_ApprovedByUserId] ON [WeightRound] ([ApprovedByUserId]);

CREATE UNIQUE INDEX [IX_WeightRound_TenderId_Version] ON [WeightRound] ([TenderId], [Version]);

ALTER TABLE [Tenders] ADD CONSTRAINT [FK_Tenders_AspNetUsers_AwardedByUserId] FOREIGN KEY ([AwardedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261113080931_WeightedTenderWorkflow', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [AspNetUsers] ADD [EmailOtpFailedAttempts] int NOT NULL DEFAULT 0;

ALTER TABLE [AspNetUsers] ADD [EmailOtpSentAt] datetime2 NULL;

ALTER TABLE [AspNetUsers] ADD [MustChangePassword] bit NOT NULL DEFAULT CAST(0 AS bit);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261113091517_AddAuthenticationState', N'10.0.14');

COMMIT;
GO

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

BEGIN TRANSACTION;
ALTER TABLE [EvaluationSession] ADD [OpenedAt] datetime2 NULL;

ALTER TABLE [EvaluationSession] ADD [ZoomMeetingId] nvarchar(40) NULL;

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

CREATE INDEX [IX_ExternalSupplierInvitation_CreatedByUserId] ON [ExternalSupplierInvitation] ([CreatedByUserId]);

CREATE UNIQUE INDEX [IX_ExternalSupplierInvitation_TenderRoundId_Email] ON [ExternalSupplierInvitation] ([TenderRoundId], [Email]);

CREATE INDEX [IX_RoundDecision_DecidedByUserId] ON [RoundDecision] ([DecidedByUserId]);

CREATE INDEX [IX_RoundDecision_RequestedByUserId] ON [RoundDecision] ([RequestedByUserId]);

CREATE INDEX [IX_RoundDecision_TenderRoundId_Kind] ON [RoundDecision] ([TenderRoundId], [Kind]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261113110000_RoundControlAndScheduling', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [ExternalSupplierInvitation] ADD [SupplierId] int NULL;

CREATE INDEX [IX_ExternalSupplierInvitation_SupplierId] ON [ExternalSupplierInvitation] ([SupplierId]);

ALTER TABLE [ExternalSupplierInvitation] ADD CONSTRAINT [FK_ExternalSupplierInvitation_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261113111426_FinalizeTenderCycle', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [FaceTemplates] (
    [Id] bigint NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ProtectedTemplate] varbinary(max) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [LastUsedAt] datetime2 NULL,
    CONSTRAINT [PK_FaceTemplates] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_FaceTemplates_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [UserPasskeyCredentials] (
    [Id] bigint NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [CredentialId] nvarchar(450) NOT NULL,
    [PublicKey] varbinary(max) NOT NULL,
    [SignCount] bigint NOT NULL,
    [DeviceLabel] nvarchar(150) NULL,
    [AddedAt] datetime2 NOT NULL,
    [LastUsedAt] datetime2 NULL,
    CONSTRAINT [PK_UserPasskeyCredentials] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UserPasskeyCredentials_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_FaceTemplates_UserId] ON [FaceTemplates] ([UserId]);

CREATE UNIQUE INDEX [IX_UserPasskeyCredentials_CredentialId] ON [UserPasskeyCredentials] ([CredentialId]);

CREATE INDEX [IX_UserPasskeyCredentials_UserId] ON [UserPasskeyCredentials] ([UserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261113133104_AddBiometricSignIn', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [Suppliers] ADD [Category] nvarchar(80) NOT NULL DEFAULT N'Other';

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261113143000_AddSupplierCategory', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [FinalDecisionNotices] (
    [Id] int NOT NULL IDENTITY,
    [TenderId] int NOT NULL,
    [TenderReportId] int NULL,
    [Status] nvarchar(30) NOT NULL,
    [TopApprovals] int NOT NULL,
    [TopVotes] int NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ManagerUserId] nvarchar(max) NULL,
    [ManagerAcknowledgedAt] datetime2 NULL,
    [ManagerComment] nvarchar(1000) NULL,
    [OfficerUserId] nvarchar(max) NULL,
    [SuppliersNotifiedAt] datetime2 NULL,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_FinalDecisionNotices] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_FinalDecisionNotices_Tenders_TenderId] FOREIGN KEY ([TenderId]) REFERENCES [Tenders] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SupplierResultDeliveries] (
    [Id] int NOT NULL IDENTITY,
    [FinalDecisionNoticeId] int NOT NULL,
    [TenderId] int NOT NULL,
    [SupplierId] int NOT NULL,
    [BidId] int NOT NULL,
    [RecipientUserId] nvarchar(450) NOT NULL,
    [Outcome] nvarchar(20) NOT NULL,
    [NotificationId] int NULL,
    [InAppStatus] nvarchar(20) NOT NULL,
    [EmailStatus] nvarchar(20) NOT NULL,
    [EmailAttempts] int NOT NULL,
    [LastError] nvarchar(500) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [EmailSentAt] datetime2 NULL,
    CONSTRAINT [PK_SupplierResultDeliveries] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SupplierResultDeliveries_FinalDecisionNotices_FinalDecisionNoticeId] FOREIGN KEY ([FinalDecisionNoticeId]) REFERENCES [FinalDecisionNotices] ([Id]) ON DELETE NO ACTION
);

CREATE UNIQUE INDEX [IX_FinalDecisionNotices_TenderId_TenderReportId] ON [FinalDecisionNotices] ([TenderId], [TenderReportId]) WHERE [TenderReportId] IS NOT NULL;

CREATE UNIQUE INDEX [IX_SupplierResultDeliveries_FinalDecisionNoticeId_RecipientUserId] ON [SupplierResultDeliveries] ([FinalDecisionNoticeId], [RecipientUserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261113144118_AddFinalDecisionRelay', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [AiChatConversations] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_AiChatConversations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AiChatConversations_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [AiChatMessages] (
    [Id] bigint NOT NULL IDENTITY,
    [ConversationId] int NOT NULL,
    [Role] nvarchar(20) NOT NULL,
    [Content] nvarchar(max) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_AiChatMessages] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AiChatMessages_AiChatConversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [AiChatConversations] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_AiChatConversations_UserId_UpdatedAt] ON [AiChatConversations] ([UserId], [UpdatedAt]);

CREATE INDEX [IX_AiChatMessages_ConversationId_Id] ON [AiChatMessages] ([ConversationId], [Id]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261113144312_AddAiChatAssistant', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM [UserPasskeyCredentials]) THROW 51000, 'Legacy passkeys exist. Export them and arrange re-enrollment before this migration; no records were deleted.', 1;

DROP TABLE [UserPasskeyCredentials];

CREATE TABLE [AspNetUserPasskeys] (
    [CredentialId] varbinary(1024) NOT NULL,
    [UserId] nvarchar(450) NOT NULL,
    [Data] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_AspNetUserPasskeys] PRIMARY KEY ([CredentialId]),
    CONSTRAINT [FK_AspNetUserPasskeys_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_AspNetUserPasskeys_UserId] ON [AspNetUserPasskeys] ([UserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261113192717_UseIdentityPasskeys', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
UPDATE u
SET EmailConfirmed = 1
FROM AspNetUsers AS u
WHERE u.EmailConfirmed = 0
  AND u.SupplierId IS NULL
  AND u.PasswordHash IS NOT NULL
  AND EXISTS (
      SELECT 1 FROM AuditLogs AS a
      WHERE a.EntityName = 'ApplicationUser'
        AND a.EntityId = u.Id
        AND a.Action = 'Created'
  );

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261113230000_ConfirmAdminCreatedEmployees', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [ReportSignature] ADD [SignatureImage] varbinary(max) NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261114013352_ReportSignatureImage', N'10.0.14');

COMMIT;
GO


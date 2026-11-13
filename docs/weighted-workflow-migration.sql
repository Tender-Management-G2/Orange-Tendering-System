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

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class WeightedTenderWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AwardApprovalEvidenceJson",
                table: "Tenders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AwardedByUserId",
                table: "Tenders",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "Tenders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Tenders",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedValue",
                table: "Tenders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsCritical",
                table: "Tenders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Tenders",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AlterColumn<decimal>(
                name: "Weight",
                table: "EvaluationCriteria",
                type: "decimal(12,6)",
                precision: 12,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(9,4)",
                oldPrecision: 9,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "TechnicalWeight",
                table: "CommitteeRounds",
                type: "decimal(12,6)",
                precision: 12,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(9,4)",
                oldPrecision: 9,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "FinancialWeight",
                table: "CommitteeRounds",
                type: "decimal(12,6)",
                precision: 12,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(9,4)",
                oldPrecision: 9,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "AiAnalysis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderRoundId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Findings = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAnalysis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiAnalysis_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiAnalysis_TenderRounds_TenderRoundId",
                        column: x => x.TenderRoundId,
                        principalTable: "TenderRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeightRound",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    RequirementsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FinancialAverage = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    TechnicalAverage = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    ApprovedFinancial = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    ApprovedTechnical = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovalPolicyJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeightRound", x => x.Id);
                    table.CheckConstraint("CK_WeightRound_Approved", "[State] <> 'Approved' OR ([ApprovedFinancial] IS NOT NULL AND [ApprovedTechnical] IS NOT NULL AND [ApprovedFinancial] + [ApprovedTechnical] = 100 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_WeightRound_AspNetUsers_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeightRound_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationSession",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderRoundId = table.Column<int>(type: "int", nullable: false),
                    WeightRoundId = table.Column<int>(type: "int", nullable: false),
                    CommitteeRoundId = table.Column<int>(type: "int", nullable: false),
                    ReportReviewRoundId = table.Column<int>(type: "int", nullable: true),
                    AiAnalysisId = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MeetingUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    State = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ClosureReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FinalApprovalPolicyJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReportId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationSession", x => x.Id);
                    table.CheckConstraint("CK_EvaluationSession_Window", "[EndsAt] > [StartsAt]");
                    table.ForeignKey(
                        name: "FK_EvaluationSession_AiAnalysis_AiAnalysisId",
                        column: x => x.AiAnalysisId,
                        principalTable: "AiAnalysis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationSession_AspNetUsers_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationSession_CommitteeRounds_CommitteeRoundId",
                        column: x => x.CommitteeRoundId,
                        principalTable: "CommitteeRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationSession_CommitteeRounds_ReportReviewRoundId",
                        column: x => x.ReportReviewRoundId,
                        principalTable: "CommitteeRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationSession_TenderReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "TenderReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationSession_TenderRounds_TenderRoundId",
                        column: x => x.TenderRoundId,
                        principalTable: "TenderRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationSession_WeightRound_WeightRoundId",
                        column: x => x.WeightRoundId,
                        principalTable: "WeightRound",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeightProposal",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WeightRoundId = table.Column<int>(type: "int", nullable: false),
                    CommitteeMemberId = table.Column<int>(type: "int", nullable: false),
                    MemberUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Financial = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Technical = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeightProposal", x => x.Id);
                    table.CheckConstraint("CK_WeightProposal_Valid", "[Financial] >= 0 AND [Financial] <= 100 AND [Technical] >= 0 AND [Technical] <= 100 AND [Financial] + [Technical] = 100");
                    table.ForeignKey(
                        name: "FK_WeightProposal_AspNetUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeightProposal_CommitteeMembers_CommitteeMemberId",
                        column: x => x.CommitteeMemberId,
                        principalTable: "CommitteeMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeightProposal_WeightRound_WeightRoundId",
                        column: x => x.WeightRoundId,
                        principalTable: "WeightRound",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationParticipation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EvaluationSessionId = table.Column<int>(type: "int", nullable: false),
                    CommitteeMemberId = table.Column<int>(type: "int", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationParticipation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationParticipation_CommitteeMembers_CommitteeMemberId",
                        column: x => x.CommitteeMemberId,
                        principalTable: "CommitteeMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationParticipation_EvaluationSession_EvaluationSessionId",
                        column: x => x.EvaluationSessionId,
                        principalTable: "EvaluationSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionBid",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EvaluationSessionId = table.Column<int>(type: "int", nullable: false),
                    BidId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionBid", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionBid_Bids_BidId",
                        column: x => x.BidId,
                        principalTable: "Bids",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionBid_EvaluationSession_EvaluationSessionId",
                        column: x => x.EvaluationSessionId,
                        principalTable: "EvaluationSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_AwardedByUserId",
                table: "Tenders",
                column: "AwardedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAnalysis_ReviewedByUserId",
                table: "AiAnalysis",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAnalysis_TenderRoundId",
                table: "AiAnalysis",
                column: "TenderRoundId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationParticipation_CommitteeMemberId",
                table: "EvaluationParticipation",
                column: "CommitteeMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationParticipation_EvaluationSessionId_CommitteeMemberId",
                table: "EvaluationParticipation",
                columns: new[] { "EvaluationSessionId", "CommitteeMemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSession_AiAnalysisId",
                table: "EvaluationSession",
                column: "AiAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSession_ClosedByUserId",
                table: "EvaluationSession",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSession_CommitteeRoundId",
                table: "EvaluationSession",
                column: "CommitteeRoundId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSession_ReportId",
                table: "EvaluationSession",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSession_ReportReviewRoundId",
                table: "EvaluationSession",
                column: "ReportReviewRoundId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSession_TenderRoundId",
                table: "EvaluationSession",
                column: "TenderRoundId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSession_WeightRoundId",
                table: "EvaluationSession",
                column: "WeightRoundId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionBid_BidId",
                table: "SessionBid",
                column: "BidId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionBid_EvaluationSessionId_BidId",
                table: "SessionBid",
                columns: new[] { "EvaluationSessionId", "BidId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeightProposal_CommitteeMemberId",
                table: "WeightProposal",
                column: "CommitteeMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_WeightProposal_MemberUserId",
                table: "WeightProposal",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WeightProposal_WeightRoundId_CommitteeMemberId",
                table: "WeightProposal",
                columns: new[] { "WeightRoundId", "CommitteeMemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeightProposal_WeightRoundId_MemberUserId",
                table: "WeightProposal",
                columns: new[] { "WeightRoundId", "MemberUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeightRound_ApprovedByUserId",
                table: "WeightRound",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WeightRound_TenderId_Version",
                table: "WeightRound",
                columns: new[] { "TenderId", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tenders_AspNetUsers_AwardedByUserId",
                table: "Tenders",
                column: "AwardedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_AspNetUsers_AwardedByUserId",
                table: "Tenders");

            migrationBuilder.DropTable(
                name: "EvaluationParticipation");

            migrationBuilder.DropTable(
                name: "SessionBid");

            migrationBuilder.DropTable(
                name: "WeightProposal");

            migrationBuilder.DropTable(
                name: "EvaluationSession");

            migrationBuilder.DropTable(
                name: "AiAnalysis");

            migrationBuilder.DropTable(
                name: "WeightRound");

            migrationBuilder.DropIndex(
                name: "IX_Tenders_AwardedByUserId",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "AwardApprovalEvidenceJson",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "AwardedByUserId",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "EstimatedValue",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "IsCritical",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Tenders");

            migrationBuilder.AlterColumn<decimal>(
                name: "Weight",
                table: "EvaluationCriteria",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(12,6)",
                oldPrecision: 12,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "TechnicalWeight",
                table: "CommitteeRounds",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(12,6)",
                oldPrecision: 12,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "FinancialWeight",
                table: "CommitteeRounds",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(12,6)",
                oldPrecision: 12,
                oldScale: 6,
                oldNullable: true);
        }
    }
}

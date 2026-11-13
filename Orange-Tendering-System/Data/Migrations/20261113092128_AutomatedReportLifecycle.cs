using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class AutomatedReportLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "PdfContent",
                table: "TenderReports",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdfSha256",
                table: "TenderReports",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TenderReports",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "SignaturePageIndex",
                table: "TenderReports",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotJson",
                table: "TenderReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotSha256",
                table: "TenderReports",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSha256",
                table: "TenderReports",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAt",
                table: "TenderReports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateVersion",
                table: "TenderReports",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReportRevision",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenderReportId = table.Column<int>(type: "int", nullable: false),
                    SourceStage = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceSessionState = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TargetStage = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Correction = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReturningUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ResponsibleUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Feedback = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RequiredAction = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PolicyEvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Blocker = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ResubmittedReportId = table.Column<int>(type: "int", nullable: true),
                    ResubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CorrectionSummary = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportRevision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportRevision_AspNetUsers_ResponsibleUserId",
                        column: x => x.ResponsibleUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportRevision_AspNetUsers_ReturningUserId",
                        column: x => x.ReturningUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportRevision_TenderReports_ResubmittedReportId",
                        column: x => x.ResubmittedReportId,
                        principalTable: "TenderReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportRevision_TenderReports_TenderReportId",
                        column: x => x.TenderReportId,
                        principalTable: "TenderReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportSignature",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderReportId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SignerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Slot = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DocumentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SnapshotHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ManifestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CmsSignature = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CertificateThumbprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    InvalidatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvalidationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportSignature", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportSignature_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportSignature_TenderReports_TenderReportId",
                        column: x => x.TenderReportId,
                        principalTable: "TenderReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportRevision_RequestId",
                table: "ReportRevision",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportRevision_ResponsibleUserId",
                table: "ReportRevision",
                column: "ResponsibleUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportRevision_ResubmittedReportId",
                table: "ReportRevision",
                column: "ResubmittedReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportRevision_ReturningUserId",
                table: "ReportRevision",
                column: "ReturningUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportRevision_TenderReportId_Status",
                table: "ReportRevision",
                columns: new[] { "TenderReportId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportSignature_TenderReportId_Stage_Slot",
                table: "ReportSignature",
                columns: new[] { "TenderReportId", "Stage", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportSignature_TenderReportId_Stage_UserId",
                table: "ReportSignature",
                columns: new[] { "TenderReportId", "Stage", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportSignature_UserId",
                table: "ReportSignature",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportRevision");

            migrationBuilder.DropTable(
                name: "ReportSignature");

            migrationBuilder.DropColumn(
                name: "PdfContent",
                table: "TenderReports");

            migrationBuilder.DropColumn(
                name: "PdfSha256",
                table: "TenderReports");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TenderReports");

            migrationBuilder.DropColumn(
                name: "SignaturePageIndex",
                table: "TenderReports");

            migrationBuilder.DropColumn(
                name: "SnapshotJson",
                table: "TenderReports");

            migrationBuilder.DropColumn(
                name: "SnapshotSha256",
                table: "TenderReports");

            migrationBuilder.DropColumn(
                name: "SourceSha256",
                table: "TenderReports");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                table: "TenderReports");

            migrationBuilder.DropColumn(
                name: "TemplateVersion",
                table: "TenderReports");
        }
    }
}

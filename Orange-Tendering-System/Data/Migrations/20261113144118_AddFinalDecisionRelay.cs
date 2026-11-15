using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinalDecisionRelay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinalDecisionNotices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    TenderReportId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TopApprovals = table.Column<int>(type: "int", nullable: false),
                    TopVotes = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ManagerUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ManagerAcknowledgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ManagerComment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OfficerUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SuppliersNotifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinalDecisionNotices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinalDecisionNotices_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierResultDeliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FinalDecisionNoticeId = table.Column<int>(type: "int", nullable: false),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    BidId = table.Column<int>(type: "int", nullable: false),
                    RecipientUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NotificationId = table.Column<int>(type: "int", nullable: true),
                    InAppStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EmailStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EmailAttempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmailSentAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierResultDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierResultDeliveries_FinalDecisionNotices_FinalDecisionNoticeId",
                        column: x => x.FinalDecisionNoticeId,
                        principalTable: "FinalDecisionNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinalDecisionNotices_TenderId_TenderReportId",
                table: "FinalDecisionNotices",
                columns: new[] { "TenderId", "TenderReportId" },
                unique: true,
                filter: "[TenderReportId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierResultDeliveries_FinalDecisionNoticeId_RecipientUserId",
                table: "SupplierResultDeliveries",
                columns: new[] { "FinalDecisionNoticeId", "RecipientUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierResultDeliveries");

            migrationBuilder.DropTable(
                name: "FinalDecisionNotices");
        }
    }
}

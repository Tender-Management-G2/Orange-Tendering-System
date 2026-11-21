using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class InternalAwardReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InternalAwardReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FinalDecisionNoticeId = table.Column<int>(type: "int", nullable: false),
                    TenderTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    AwardedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OfficerNotificationId = table.Column<int>(type: "int", nullable: false),
                    ManagerNotificationId = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AiWritten = table.Column<bool>(type: "bit", nullable: false),
                    OfficerEmailSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ManagerEmailSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmailAttempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalAwardReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InternalAwardReports_FinalDecisionNotices_FinalDecisionNoticeId",
                        column: x => x.FinalDecisionNoticeId,
                        principalTable: "FinalDecisionNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InternalAwardReports_Notifications_ManagerNotificationId",
                        column: x => x.ManagerNotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InternalAwardReports_Notifications_OfficerNotificationId",
                        column: x => x.OfficerNotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InternalAwardReports_FinalDecisionNoticeId",
                table: "InternalAwardReports",
                column: "FinalDecisionNoticeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InternalAwardReports_ManagerNotificationId",
                table: "InternalAwardReports",
                column: "ManagerNotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalAwardReports_OfficerNotificationId",
                table: "InternalAwardReports",
                column: "OfficerNotificationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InternalAwardReports");
        }
    }
}

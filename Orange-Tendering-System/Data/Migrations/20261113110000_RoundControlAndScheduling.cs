using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    // Additive only: two new tables and two nullable columns. No existing row is changed or removed.
    // Hand-authored (the authoring environment could not run "dotnet ef"); names follow EF Core defaults.
    public partial class RoundControlAndScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OpenedAt",
                table: "EvaluationSession",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomMeetingId",
                table: "EvaluationSession",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExternalSupplierInvitation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderRoundId = table.Column<int>(type: "int", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalSupplierInvitation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalSupplierInvitation_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalSupplierInvitation_TenderRounds_TenderRoundId",
                        column: x => x.TenderRoundId,
                        principalTable: "TenderRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoundDecision",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderRoundId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OfferCount = table.Column<int>(type: "int", nullable: false),
                    Justification = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundDecision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoundDecision_AspNetUsers_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoundDecision_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoundDecision_TenderRounds_TenderRoundId",
                        column: x => x.TenderRoundId,
                        principalTable: "TenderRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalSupplierInvitation_CreatedByUserId",
                table: "ExternalSupplierInvitation",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalSupplierInvitation_TenderRoundId_Email",
                table: "ExternalSupplierInvitation",
                columns: new[] { "TenderRoundId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoundDecision_DecidedByUserId",
                table: "RoundDecision",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoundDecision_RequestedByUserId",
                table: "RoundDecision",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoundDecision_TenderRoundId_Kind",
                table: "RoundDecision",
                columns: new[] { "TenderRoundId", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalSupplierInvitation");

            migrationBuilder.DropTable(
                name: "RoundDecision");

            migrationBuilder.DropColumn(
                name: "OpenedAt",
                table: "EvaluationSession");

            migrationBuilder.DropColumn(
                name: "ZoomMeetingId",
                table: "EvaluationSession");
        }
    }
}

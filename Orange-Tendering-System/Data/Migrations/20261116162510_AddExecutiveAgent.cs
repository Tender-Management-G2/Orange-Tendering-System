using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutiveAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecutiveAgentActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolIndex = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SnapshotHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DedupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReportId = table.Column<int>(type: "int", nullable: true),
                    DocumentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExecutedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ManifestJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProofChallenge = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ProofExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WebAuthnAssertionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CmsSignature = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CertificateThumbprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutiveAgentActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutiveAgentRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    InputHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutiveAgentRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutiveAgentActions_DedupHash",
                table: "ExecutiveAgentActions",
                column: "DedupHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutiveAgentActions_RunId_ToolIndex",
                table: "ExecutiveAgentActions",
                columns: new[] { "RunId", "ToolIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutiveAgentRuns_UserId_CreatedAt",
                table: "ExecutiveAgentRuns",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutiveAgentActions");

            migrationBuilder.DropTable(
                name: "ExecutiveAgentRuns");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdminAnalyticsAccessAndContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinalTenderContracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinalTenderContracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinalTenderContracts_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportRoleAccess",
                columns: table => new
                {
                    ReportKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Allowed = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportRoleAccess", x => new { x.ReportKey, x.Role });
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinalTenderContracts_TenderId_Sha256",
                table: "FinalTenderContracts",
                columns: new[] { "TenderId", "Sha256" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinalTenderContracts");

            migrationBuilder.DropTable(
                name: "ReportRoleAccess");
        }
    }
}

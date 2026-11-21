using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTopCommitteeElectronicSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Documents",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureDocumentHash",
                table: "CommitteeVotes",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SignatureReportId",
                table: "CommitteeVotes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeVotes_SignatureReportId",
                table: "CommitteeVotes",
                column: "SignatureReportId");

            migrationBuilder.AddForeignKey(
                name: "FK_CommitteeVotes_TenderReports_SignatureReportId",
                table: "CommitteeVotes",
                column: "SignatureReportId",
                principalTable: "TenderReports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CommitteeVotes_TenderReports_SignatureReportId",
                table: "CommitteeVotes");

            migrationBuilder.DropIndex(
                name: "IX_CommitteeVotes_SignatureReportId",
                table: "CommitteeVotes");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "SignatureDocumentHash",
                table: "CommitteeVotes");

            migrationBuilder.DropColumn(
                name: "SignatureReportId",
                table: "CommitteeVotes");
        }
    }
}

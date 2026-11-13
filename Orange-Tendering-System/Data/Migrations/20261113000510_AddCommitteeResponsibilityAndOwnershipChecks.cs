using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitteeResponsibilityAndOwnershipChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Responsibility",
                table: "CommitteeMembers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Documents_ExactlyOneOwner",
                table: "Documents",
                sql: "(CASE WHEN [SupplierId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [PurchaseRequestId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [TenderId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BidId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [ClarificationMessageId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [TenderReportId] IS NULL THEN 0 ELSE 1 END) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeMembers_TenderId_CommitteeType_Responsibility",
                table: "CommitteeMembers",
                columns: new[] { "TenderId", "CommitteeType", "Responsibility" },
                unique: true,
                filter: "[Responsibility] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Clarifications_HasContext",
                table: "Clarifications",
                sql: "[PurchaseRequestId] IS NOT NULL OR [TenderId] IS NOT NULL OR [BidId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Documents_ExactlyOneOwner",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_CommitteeMembers_TenderId_CommitteeType_Responsibility",
                table: "CommitteeMembers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Clarifications_HasContext",
                table: "Clarifications");

            migrationBuilder.DropColumn(
                name: "Responsibility",
                table: "CommitteeMembers");
        }
    }
}

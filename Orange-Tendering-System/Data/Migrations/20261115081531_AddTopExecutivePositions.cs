using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTopExecutivePositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExecutivePosition",
                table: "CommitteeMembers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeMembers_TenderId_ExecutivePosition",
                table: "CommitteeMembers",
                columns: new[] { "TenderId", "ExecutivePosition" },
                unique: true,
                filter: "[ExecutivePosition] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommitteeMember_ExecutivePosition",
                table: "CommitteeMembers",
                sql: "[ExecutivePosition] IS NULL OR ([CommitteeType] = 'Top' AND [ExecutivePosition] IN ('CEO','CFO','CTO','COO'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CommitteeMembers_TenderId_ExecutivePosition",
                table: "CommitteeMembers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CommitteeMember_ExecutivePosition",
                table: "CommitteeMembers");

            migrationBuilder.DropColumn(
                name: "ExecutivePosition",
                table: "CommitteeMembers");
        }
    }
}

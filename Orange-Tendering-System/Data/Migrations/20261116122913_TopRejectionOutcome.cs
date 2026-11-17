using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class TopRejectionOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NewRoundNumber",
                table: "FinalDecisionNotices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "FinalDecisionNotices",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Award"); // Existing notices are approvals.

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "FinalDecisionNotices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedByUserId",
                table: "FinalDecisionNotices",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "FinalDecisionNotices",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewRoundNumber",
                table: "FinalDecisionNotices");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "FinalDecisionNotices");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "FinalDecisionNotices");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                table: "FinalDecisionNotices");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "FinalDecisionNotices");
        }
    }
}

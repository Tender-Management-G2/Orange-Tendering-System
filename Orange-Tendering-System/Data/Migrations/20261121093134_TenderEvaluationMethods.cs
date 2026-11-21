using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class TenderEvaluationMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TechnicalCriteriaJson",
                table: "WeightRound",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvaluationMethod",
                table: "PurchaseRequests",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "AiAnalysisId",
                table: "EvaluationSession",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "TechnicalBreakdownJson",
                table: "EvaluationScores",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TechnicalCriteriaJson",
                table: "WeightRound");

            migrationBuilder.DropColumn(
                name: "EvaluationMethod",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "TechnicalBreakdownJson",
                table: "EvaluationScores");

            migrationBuilder.AlterColumn<int>(
                name: "AiAnalysisId",
                table: "EvaluationSession",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}

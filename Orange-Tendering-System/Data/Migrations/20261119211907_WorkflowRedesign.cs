using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequiredDocuments",
                table: "Tenders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "TenderItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "PurchaseRequestItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Concerns",
                table: "EvaluationParticipation",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecommendationReason",
                table: "EvaluationParticipation",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecommendedBidId",
                table: "EvaluationParticipation",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "TaxAmount",
                table: "Bids",
                type: "decimal(19,3)",
                precision: 19,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "ShippingCost",
                table: "Bids",
                type: "decimal(19,3)",
                precision: 19,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "OtherCost",
                table: "Bids",
                type: "decimal(19,3)",
                precision: 19,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "MaintenanceCost",
                table: "Bids",
                type: "decimal(19,3)",
                precision: 19,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "InstallationCost",
                table: "Bids",
                type: "decimal(19,3)",
                precision: 19,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<string>(
                name: "AfterSalesSupport",
                table: "Bids",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Deviations",
                table: "Bids",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaintenanceDescription",
                table: "Bids",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OtherCostDescription",
                table: "Bids",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TaxIncludesCharges",
                table: "Bids",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "Bids",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarrantyMonths",
                table: "Bids",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarrantyTerms",
                table: "Bids",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarrantyType",
                table: "Bids",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "BidItems",
                type: "decimal(19,3)",
                precision: 19,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "BidItems",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfferedProduct",
                table: "BidItems",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PreTenderReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ApprovalsRequired = table.Column<int>(type: "int", nullable: false),
                    SubmittedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OfficerNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreTenderReviews", x => x.Id);
                    table.CheckConstraint("CK_PreTenderReview_Threshold", "[ApprovalsRequired] >= 1 AND [ApprovalsRequired] <= 4");
                    table.ForeignKey(
                        name: "FK_PreTenderReviews_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcurementCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcurementCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PreTenderVotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PreTenderReviewId = table.Column<int>(type: "int", nullable: false),
                    CommitteeMemberId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    VotedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreTenderVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PreTenderVotes_CommitteeMembers_CommitteeMemberId",
                        column: x => x.CommitteeMemberId,
                        principalTable: "CommitteeMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PreTenderVotes_PreTenderReviews_PreTenderReviewId",
                        column: x => x.PreTenderReviewId,
                        principalTable: "PreTenderReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenderItems_CategoryId",
                table: "TenderItems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestItems_CategoryId",
                table: "PurchaseRequestItems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationParticipation_RecommendedBidId",
                table: "EvaluationParticipation",
                column: "RecommendedBidId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Bids_TaxRate",
                table: "Bids",
                sql: "[TaxRate] IS NULL OR ([TaxRate] >= 0 AND [TaxRate] <= 100)");

            migrationBuilder.CreateIndex(
                name: "IX_PreTenderReviews_TenderId_Attempt",
                table: "PreTenderReviews",
                columns: new[] { "TenderId", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PreTenderVotes_CommitteeMemberId",
                table: "PreTenderVotes",
                column: "CommitteeMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_PreTenderVotes_PreTenderReviewId_CommitteeMemberId",
                table: "PreTenderVotes",
                columns: new[] { "PreTenderReviewId", "CommitteeMemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementCategories_Name",
                table: "ProcurementCategories",
                column: "Name",
                unique: true);

            // Starting categories. The names match the supplier registration categories so the Sourcing Officer can
            // match tender items to suppliers; Admin can add, rename or deactivate them afterwards.
            var categories = new[]
            {
                "IT & Electronics", "Telecommunications", "Software & Licenses", "Office Supplies", "Furniture & Fixtures",
                "Electrical Equipment", "Construction & Maintenance", "Medical & Safety", "Cleaning & Facilities",
                "Transport & Logistics", "Food & Hospitality", "Professional Services", "Marketing & Advertising", "Other"
            };
            for (var i = 0; i < categories.Length; i++)
                migrationBuilder.InsertData(
                    table: "ProcurementCategories",
                    columns: new[] { "Name", "IsActive", "SortOrder", "CreatedAt" },
                    values: new object[] { categories[i], true, (i + 1) * 10, new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.AddForeignKey(
                name: "FK_EvaluationParticipation_Bids_RecommendedBidId",
                table: "EvaluationParticipation",
                column: "RecommendedBidId",
                principalTable: "Bids",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseRequestItems_ProcurementCategories_CategoryId",
                table: "PurchaseRequestItems",
                column: "CategoryId",
                principalTable: "ProcurementCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TenderItems_ProcurementCategories_CategoryId",
                table: "TenderItems",
                column: "CategoryId",
                principalTable: "ProcurementCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EvaluationParticipation_Bids_RecommendedBidId",
                table: "EvaluationParticipation");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseRequestItems_ProcurementCategories_CategoryId",
                table: "PurchaseRequestItems");

            migrationBuilder.DropForeignKey(
                name: "FK_TenderItems_ProcurementCategories_CategoryId",
                table: "TenderItems");

            migrationBuilder.DropTable(
                name: "PreTenderVotes");

            migrationBuilder.DropTable(
                name: "ProcurementCategories");

            migrationBuilder.DropTable(
                name: "PreTenderReviews");

            migrationBuilder.DropIndex(
                name: "IX_TenderItems_CategoryId",
                table: "TenderItems");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseRequestItems_CategoryId",
                table: "PurchaseRequestItems");

            migrationBuilder.DropIndex(
                name: "IX_EvaluationParticipation_RecommendedBidId",
                table: "EvaluationParticipation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Bids_TaxRate",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "RequiredDocuments",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "PurchaseRequestItems");

            migrationBuilder.DropColumn(
                name: "Concerns",
                table: "EvaluationParticipation");

            migrationBuilder.DropColumn(
                name: "RecommendationReason",
                table: "EvaluationParticipation");

            migrationBuilder.DropColumn(
                name: "RecommendedBidId",
                table: "EvaluationParticipation");

            migrationBuilder.DropColumn(
                name: "AfterSalesSupport",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "Deviations",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "MaintenanceDescription",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "OtherCostDescription",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "TaxIncludesCharges",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "WarrantyMonths",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "WarrantyTerms",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "WarrantyType",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "BidItems");

            migrationBuilder.DropColumn(
                name: "OfferedProduct",
                table: "BidItems");

            migrationBuilder.AlterColumn<decimal>(
                name: "TaxAmount",
                table: "Bids",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,3)",
                oldPrecision: 19,
                oldScale: 3);

            migrationBuilder.AlterColumn<decimal>(
                name: "ShippingCost",
                table: "Bids",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,3)",
                oldPrecision: 19,
                oldScale: 3);

            migrationBuilder.AlterColumn<decimal>(
                name: "OtherCost",
                table: "Bids",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,3)",
                oldPrecision: 19,
                oldScale: 3);

            migrationBuilder.AlterColumn<decimal>(
                name: "MaintenanceCost",
                table: "Bids",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,3)",
                oldPrecision: 19,
                oldScale: 3);

            migrationBuilder.AlterColumn<decimal>(
                name: "InstallationCost",
                table: "Bids",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,3)",
                oldPrecision: 19,
                oldScale: 3);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "BidItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,3)",
                oldPrecision: 19,
                oldScale: 3);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class FinalizeTenderCycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SupplierId",
                table: "ExternalSupplierInvitation",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalSupplierInvitation_SupplierId",
                table: "ExternalSupplierInvitation",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExternalSupplierInvitation_Suppliers_SupplierId",
                table: "ExternalSupplierInvitation",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExternalSupplierInvitation_Suppliers_SupplierId",
                table: "ExternalSupplierInvitation");

            migrationBuilder.DropIndex(
                name: "IX_ExternalSupplierInvitation_SupplierId",
                table: "ExternalSupplierInvitation");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "ExternalSupplierInvitation");
        }
    }
}

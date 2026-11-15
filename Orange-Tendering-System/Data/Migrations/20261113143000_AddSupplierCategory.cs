using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Orange_Tendering_System.Data;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261113143000_AddSupplierCategory")]
public sealed class AddSupplierCategory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "Category", table: "Suppliers", type: "nvarchar(80)", maxLength: 80,
        nullable: false, defaultValue: "Other");

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "Category", table: "Suppliers");
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Orange_Tendering_System.Data.Migrations;

// Repair employees created through AdminService.CreateUserAsync before that path
// started confirming accounts. The audit row identifies that exact creation path;
// supplier registrations and activation-link employees are intentionally excluded.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261113230000_ConfirmAdminCreatedEmployees")]
public sealed class ConfirmAdminCreatedEmployees : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE u
            SET EmailConfirmed = 1
            FROM AspNetUsers AS u
            WHERE u.EmailConfirmed = 0
              AND u.SupplierId IS NULL
              AND u.PasswordHash IS NOT NULL
              AND EXISTS (
                  SELECT 1 FROM AuditLogs AS a
                  WHERE a.EntityName = 'ApplicationUser'
                    AND a.EntityId = u.Id
                    AND a.Action = 'Created'
              );
            """);
    }

    // Confirmation cannot be reversed safely after an account has started using it.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}

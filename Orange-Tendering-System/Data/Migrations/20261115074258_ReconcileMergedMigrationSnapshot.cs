using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <summary>
    /// Reconciles migration model metadata after parallel branches merged.
    /// Earlier migrations already added the document hash and committee signature columns.
    /// </summary>
    public partial class ReconcileMergedMigrationSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

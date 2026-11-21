using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orange_Tendering_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class AiChatWorkspaceScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkspaceRole",
                table: "AiChatConversations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkspaceRole",
                table: "AiChatConversations");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.MVC.Migrations
{
    /// <inheritdoc />
    public partial class AddReplyFlowFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Action",
                table: "WhatsAppPendingClarifications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "clarification");

            migrationBuilder.AddColumn<int>(
                name: "CreatedBy",
                table: "WhatsAppMessageHistory",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowLevel",
                table: "WhatsAppMessageHistory",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Action",
                table: "WhatsAppPendingClarifications");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "WhatsAppMessageHistory");

            migrationBuilder.DropColumn(
                name: "WorkflowLevel",
                table: "WhatsAppMessageHistory");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.MVC.Migrations
{
    /// <inheritdoc />
    public partial class AddApproverRoleToWorkflowStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApproverRoleId",
                table: "WorkflowSteps",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSteps_ApproverRoleId",
                table: "WorkflowSteps",
                column: "ApproverRoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowSteps_Roles_ApproverRoleId",
                table: "WorkflowSteps",
                column: "ApproverRoleId",
                principalTable: "Roles",
                principalColumn: "RoleId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowSteps_Roles_ApproverRoleId",
                table: "WorkflowSteps");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowSteps_ApproverRoleId",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "ApproverRoleId",
                table: "WorkflowSteps");
        }
    }
}

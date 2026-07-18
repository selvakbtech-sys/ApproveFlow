using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.MVC.Migrations
{
    /// <inheritdoc />
    public partial class AddStepLevelToNotificationRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowNotificationRoles_WorkflowId_RoleId",
                table: "WorkflowNotificationRoles");

            migrationBuilder.AddColumn<int>(
                name: "StepLevel",
                table: "WorkflowNotificationRoles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowNotificationRoles_WorkflowId_StepLevel_RoleId",
                table: "WorkflowNotificationRoles",
                columns: new[] { "WorkflowId", "StepLevel", "RoleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowNotificationRoles_WorkflowId_StepLevel_RoleId",
                table: "WorkflowNotificationRoles");

            migrationBuilder.DropColumn(
                name: "StepLevel",
                table: "WorkflowNotificationRoles");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowNotificationRoles_WorkflowId_RoleId",
                table: "WorkflowNotificationRoles",
                columns: new[] { "WorkflowId", "RoleId" },
                unique: true);
        }
    }
}

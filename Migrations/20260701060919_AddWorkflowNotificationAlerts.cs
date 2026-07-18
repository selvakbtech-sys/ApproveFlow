using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.MVC.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowNotificationAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RequestViewTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestViewTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestViewTokens_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowNotificationRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowNotificationRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowNotificationRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowNotificationRoles_WorkflowDefinitions_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "WorkflowDefinitions",
                        principalColumn: "WorkflowId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestViewTokens_RequestId",
                table: "RequestViewTokens",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestViewTokens_Token",
                table: "RequestViewTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowNotificationRoles_RoleId",
                table: "WorkflowNotificationRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowNotificationRoles_WorkflowId_RoleId",
                table: "WorkflowNotificationRoles",
                columns: new[] { "WorkflowId", "RoleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestViewTokens");

            migrationBuilder.DropTable(
                name: "WorkflowNotificationRoles");
        }
    }
}

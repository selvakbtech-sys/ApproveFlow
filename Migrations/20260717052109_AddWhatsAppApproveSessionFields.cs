using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.MVC.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppApproveSessionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalRemark",
                table: "WhatsAppPendingClarifications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedOn",
                table: "WhatsAppPendingClarifications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresOn",
                table: "WhatsAppPendingClarifications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentMessageId",
                table: "WhatsAppPendingClarifications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "WhatsAppPendingClarifications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppMessageId",
                table: "WhatsAppPendingClarifications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppPendingClarifications_Action_Status_ExpiresOn",
                table: "WhatsAppPendingClarifications",
                columns: new[] { "Action", "Status", "ExpiresOn" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppPendingClarifications_ParentMessageId",
                table: "WhatsAppPendingClarifications",
                column: "ParentMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppPendingClarifications_WhatsAppMessageId",
                table: "WhatsAppPendingClarifications",
                column: "WhatsAppMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppPendingClarifications_Action_Status_ExpiresOn",
                table: "WhatsAppPendingClarifications");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppPendingClarifications_ParentMessageId",
                table: "WhatsAppPendingClarifications");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppPendingClarifications_WhatsAppMessageId",
                table: "WhatsAppPendingClarifications");

            migrationBuilder.DropColumn(
                name: "ApprovalRemark",
                table: "WhatsAppPendingClarifications");

            migrationBuilder.DropColumn(
                name: "CompletedOn",
                table: "WhatsAppPendingClarifications");

            migrationBuilder.DropColumn(
                name: "ExpiresOn",
                table: "WhatsAppPendingClarifications");

            migrationBuilder.DropColumn(
                name: "ParentMessageId",
                table: "WhatsAppPendingClarifications");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "WhatsAppPendingClarifications");

            migrationBuilder.DropColumn(
                name: "WhatsAppMessageId",
                table: "WhatsAppPendingClarifications");
        }
    }
}

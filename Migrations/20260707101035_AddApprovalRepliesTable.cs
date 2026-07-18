using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.MVC.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalRepliesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalReplies",
                columns: table => new
                {
                    ApprovalReplyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApprovalId = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    ReplyMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttachmentFileName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttachmentUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WhatsAppMessageId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalReplies", x => x.ApprovalReplyId);
                    table.ForeignKey(
                        name: "FK_ApprovalReplies_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalReplies_ApprovalId",
                table: "ApprovalReplies",
                column: "ApprovalId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalReplies_RequestId",
                table: "ApprovalReplies",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalReplies");
        }
    }
}

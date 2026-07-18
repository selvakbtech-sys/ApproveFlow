using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.MVC.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppMessageHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppMessageHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: true),
                    WorkflowId = table.Column<int>(type: "int", nullable: true),
                    MessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MobileNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    MessageText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppMessageHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessageHistory_RequestId",
                table: "WhatsAppMessageHistory",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppMessageHistory");
        }
    }
}

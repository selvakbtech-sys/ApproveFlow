using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.MVC.Migrations
{
    /// <summary>
    /// Record-wise remark history and field-level audit for DWO discussion points:
    /// new DwoRemarks table (append-only Requester / HOD / Management threads),
    /// DwoHistory.RoleKind + Changes (old → new values on an Edit), and
    /// DwoPoints.HeldFromStatus (where a held point resumes to). Hand-written (no
    /// Designer); the model snapshot carries the change.
    /// </summary>
    [DbContext(typeof(ApproveFlow.MVC.Data.AppDbContext))]
    [Migration("20260919120000_AddDwoRemarks")]
    public partial class AddDwoRemarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HeldFromStatus",
                table: "DwoPoints",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoleKind",
                table: "DwoHistory",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Changes",
                table: "DwoHistory",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DwoRemarks",
                columns: table => new
                {
                    RemarkId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PointId = table.Column<int>(type: "int", nullable: false),
                    RoleKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Remark = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DwoRemarks", x => x.RemarkId);
                    table.ForeignKey(
                        name: "FK_DwoRemarks_DwoPoints_PointId",
                        column: x => x.PointId,
                        principalTable: "DwoPoints",
                        principalColumn: "PointId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DwoRemarks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DwoRemarks_PointId",
                table: "DwoRemarks",
                column: "PointId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoRemarks_UserId",
                table: "DwoRemarks",
                column: "UserId");

            // Seed the threads from the review remarks already on file, so existing
            // points show their HOD / Management remark instead of an empty thread.
            migrationBuilder.Sql(@"
INSERT INTO dbo.DwoRemarks (PointId, RoleKind, UserId, UserName, Action, Remark, CreatedAt)
SELECT p.PointId,
       CASE WHEN p.Status IN (N'Change Requested', N'Rejected') THEN N'HOD' ELSE N'Management' END,
       p.ReviewedByUserId, ISNULL(u.FullName, N''),
       CASE p.Status WHEN N'Change Requested' THEN N'Clarification'
                     WHEN N'Rejected'         THEN N'Reject'
                     WHEN N'Hold'             THEN N'Hold'
                     WHEN N'Closed'           THEN N'Close'
                     WHEN N'Follow-up'        THEN N'Follow-up'
                     WHEN N'Reassigned'       THEN N'Reassign'
                     ELSE N'Edit' END,
       p.ReviewRemarks, ISNULL(p.ReviewedAt, p.UpdatedAt)
FROM dbo.DwoPoints p
LEFT JOIN dbo.Users u ON u.UserId = p.ReviewedByUserId
WHERE p.ReviewRemarks IS NOT NULL AND p.ReviewRemarks <> N''
  AND NOT EXISTS (SELECT 1 FROM dbo.DwoRemarks r WHERE r.PointId = p.PointId);

UPDATE dbo.DwoPoints SET HeldFromStatus = N'Management Discussion' WHERE Status = N'Hold' AND HeldFromStatus IS NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DwoRemarks");
            migrationBuilder.DropColumn(name: "Changes", table: "DwoHistory");
            migrationBuilder.DropColumn(name: "RoleKind", table: "DwoHistory");
            migrationBuilder.DropColumn(name: "HeldFromStatus", table: "DwoPoints");
        }
    }
}

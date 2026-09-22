using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.MVC.Migrations
{
    /// <inheritdoc />
    public partial class AddDwoModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    CompanyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.CompanyId);
                });

            migrationBuilder.CreateTable(
                name: "DwoSets",
                columns: table => new
                {
                    SetId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SetNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    RequestedAppointmentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AppointmentNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AppointmentStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DwoSets", x => x.SetId);
                    table.ForeignKey(
                        name: "FK_DwoSets_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DwoSets_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            // NOTE — WhatsAppOutbox deliberately omitted.
            // Add-Migration also emitted a CreateTable for WhatsAppOutbox and its index.
            // That is pre-existing drift, not part of this module: the table was added to
            // the model and created in every database by a hand-run script, so it was never
            // captured in a migration or the model snapshot. Creating it here fails with
            // "There is already an object named 'WhatsAppOutbox'", and the generated Down()
            // would have DROPPED the live outbox. Both operations were removed; the snapshot
            // now carries the table, so it will not be re-emitted again.
            migrationBuilder.CreateTable(
                name: "DwoPoints",
                columns: table => new
                {
                    PointId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PointNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TaskId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    RaisedByUserId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SetId = table.Column<int>(type: "int", nullable: true),
                    OriginPointId = table.Column<int>(type: "int", nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewRemarks = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DwoPoints", x => x.PointId);
                    table.ForeignKey(
                        name: "FK_DwoPoints_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "CompanyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DwoPoints_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DwoPoints_DwoPoints_OriginPointId",
                        column: x => x.OriginPointId,
                        principalTable: "DwoPoints",
                        principalColumn: "PointId");
                    table.ForeignKey(
                        name: "FK_DwoPoints_DwoSets_SetId",
                        column: x => x.SetId,
                        principalTable: "DwoSets",
                        principalColumn: "SetId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DwoPoints_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "LocationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DwoPoints_Users_RaisedByUserId",
                        column: x => x.RaisedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DwoPoints_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DwoAttachments",
                columns: table => new
                {
                    AttachmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PointId = table.Column<int>(type: "int", nullable: true),
                    SetId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DwoAttachments", x => x.AttachmentId);
                    table.ForeignKey(
                        name: "FK_DwoAttachments_DwoPoints_PointId",
                        column: x => x.PointId,
                        principalTable: "DwoPoints",
                        principalColumn: "PointId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DwoAttachments_DwoSets_SetId",
                        column: x => x.SetId,
                        principalTable: "DwoSets",
                        principalColumn: "SetId");
                    table.ForeignKey(
                        name: "FK_DwoAttachments_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DwoFollowUps",
                columns: table => new
                {
                    FollowUpId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PointId = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedByUserId = table.Column<int>(type: "int", nullable: true),
                    CompletionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DwoFollowUps", x => x.FollowUpId);
                    table.ForeignKey(
                        name: "FK_DwoFollowUps_DwoPoints_PointId",
                        column: x => x.PointId,
                        principalTable: "DwoPoints",
                        principalColumn: "PointId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DwoFollowUps_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DwoFollowUps_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DwoHistory",
                columns: table => new
                {
                    HistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PointId = table.Column<int>(type: "int", nullable: true),
                    SetId = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DwoHistory", x => x.HistoryId);
                    table.ForeignKey(
                        name: "FK_DwoHistory_DwoPoints_PointId",
                        column: x => x.PointId,
                        principalTable: "DwoPoints",
                        principalColumn: "PointId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DwoHistory_DwoSets_SetId",
                        column: x => x.SetId,
                        principalTable: "DwoSets",
                        principalColumn: "SetId");
                    table.ForeignKey(
                        name: "FK_DwoHistory_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DwoReferenceLinks",
                columns: table => new
                {
                    LinkId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PointId = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DwoReferenceLinks", x => x.LinkId);
                    table.ForeignKey(
                        name: "FK_DwoReferenceLinks_DwoPoints_PointId",
                        column: x => x.PointId,
                        principalTable: "DwoPoints",
                        principalColumn: "PointId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Code",
                table: "Companies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DwoAttachments_PointId",
                table: "DwoAttachments",
                column: "PointId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoAttachments_SetId",
                table: "DwoAttachments",
                column: "SetId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoAttachments_UploadedByUserId",
                table: "DwoAttachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoFollowUps_CompletedByUserId",
                table: "DwoFollowUps",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoFollowUps_OwnerUserId_DueDate",
                table: "DwoFollowUps",
                columns: new[] { "OwnerUserId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DwoFollowUps_PointId",
                table: "DwoFollowUps",
                column: "PointId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoHistory_ActorUserId",
                table: "DwoHistory",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoHistory_PointId",
                table: "DwoHistory",
                column: "PointId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoHistory_SetId",
                table: "DwoHistory",
                column: "SetId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoPoints_CompanyId",
                table: "DwoPoints",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoPoints_DepartmentId_Status",
                table: "DwoPoints",
                columns: new[] { "DepartmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DwoPoints_LocationId",
                table: "DwoPoints",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoPoints_OriginPointId",
                table: "DwoPoints",
                column: "OriginPointId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoPoints_PointNumber",
                table: "DwoPoints",
                column: "PointNumber",
                unique: true,
                filter: "[PointNumber] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_DwoPoints_RaisedByUserId_Status",
                table: "DwoPoints",
                columns: new[] { "RaisedByUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DwoPoints_ReviewedByUserId",
                table: "DwoPoints",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoPoints_SetId",
                table: "DwoPoints",
                column: "SetId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoReferenceLinks_PointId",
                table: "DwoReferenceLinks",
                column: "PointId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoSets_DepartmentId",
                table: "DwoSets",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DwoSets_SetNumber",
                table: "DwoSets",
                column: "SetNumber",
                unique: true,
                filter: "[SetNumber] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_DwoSets_SubmittedByUserId",
                table: "DwoSets",
                column: "SubmittedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DwoAttachments");

            migrationBuilder.DropTable(
                name: "DwoFollowUps");

            migrationBuilder.DropTable(
                name: "DwoHistory");

            migrationBuilder.DropTable(
                name: "DwoReferenceLinks");

            migrationBuilder.DropTable(
                name: "DwoPoints");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "DwoSets");
        }
    }
}

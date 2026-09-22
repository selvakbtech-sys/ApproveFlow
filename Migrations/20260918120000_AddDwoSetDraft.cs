using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.MVC.Migrations
{
    /// <summary>
    /// Draft Sets: adds DwoSets.Status (Draft | Submitted; every existing row is Submitted)
    /// and lets RequestedAppointmentDate be null while a Set is still a Draft. Hand-written
    /// (no Designer); the model snapshot carries the matching change.
    /// </summary>
    [DbContext(typeof(ApproveFlow.MVC.Data.AppDbContext))]
    [Migration("20260918120000_AddDwoSetDraft")]
    public partial class AddDwoSetDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "DwoSets",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Submitted");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "RequestedAppointmentDate",
                table: "DwoSets",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE dbo.DwoPoints SET SetId = NULL WHERE SetId IN (SELECT SetId FROM dbo.DwoSets WHERE Status = N'Draft');");
            migrationBuilder.Sql("DELETE FROM dbo.DwoAttachments WHERE SetId IN (SELECT SetId FROM dbo.DwoSets WHERE Status = N'Draft');");
            migrationBuilder.Sql("DELETE FROM dbo.DwoHistory WHERE SetId IN (SELECT SetId FROM dbo.DwoSets WHERE Status = N'Draft');");
            migrationBuilder.Sql("DELETE FROM dbo.DwoSets WHERE Status = N'Draft';");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "RequestedAppointmentDate",
                table: "DwoSets",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "Status",
                table: "DwoSets");
        }
    }
}

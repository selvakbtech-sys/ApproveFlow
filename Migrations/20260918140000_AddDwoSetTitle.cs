using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.MVC.Migrations
{
    /// <summary>
    /// Optional working title on a Set (shown in the "Add to Draft Set" picker and beside
    /// the DWO number). Hand-written (no Designer); the model snapshot carries the change.
    /// </summary>
    [DbContext(typeof(ApproveFlow.MVC.Data.AppDbContext))]
    [Migration("20260918140000_AddDwoSetTitle")]
    public partial class AddDwoSetTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "DwoSets",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Title",
                table: "DwoSets");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHub.Modules.MealPlan.Plan.Migrations
{
    /// <inheritdoc />
    public partial class Courses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Dinners",
                table: "Dinners");

            migrationBuilder.AddColumn<int>(
                name: "Course",
                table: "Dinners",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Dinners",
                table: "Dinners",
                columns: new[] { "Date", "Course" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Dinners",
                table: "Dinners");

            migrationBuilder.DropColumn(
                name: "Course",
                table: "Dinners");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Dinners",
                table: "Dinners",
                column: "Date");
        }
    }
}

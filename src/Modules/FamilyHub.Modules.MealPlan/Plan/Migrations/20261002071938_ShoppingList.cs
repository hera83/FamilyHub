using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyHub.Modules.MealPlan.Plan.Migrations
{
    /// <inheritdoc />
    public partial class ShoppingList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FixedItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Week = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GroceryRules",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsStaple = table.Column<bool>(type: "INTEGER", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    PackMeasure = table.Column<int>(type: "INTEGER", nullable: true),
                    PackSizes = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Section = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroceryRules", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingMarks",
                columns: table => new
                {
                    Week = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Marks = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingMarks", x => new { x.Week, x.Key });
                });

            migrationBuilder.CreateIndex(
                name: "IX_FixedItems_Week",
                table: "FixedItems",
                column: "Week");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FixedItems");

            migrationBuilder.DropTable(
                name: "GroceryRules");

            migrationBuilder.DropTable(
                name: "ShoppingMarks");
        }
    }
}

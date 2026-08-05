using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalAgent.Migrations
{
    /// <inheritdoc />
    public partial class ExpandMealLogNutrition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "FiberGrams",
                table: "MealLogs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MealType",
                table: "MealLogs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "PotassiumMilligrams",
                table: "MealLogs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SodiumMilligrams",
                table: "MealLogs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SugarGrams",
                table: "MealLogs",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FiberGrams",
                table: "MealLogs");

            migrationBuilder.DropColumn(
                name: "MealType",
                table: "MealLogs");

            migrationBuilder.DropColumn(
                name: "PotassiumMilligrams",
                table: "MealLogs");

            migrationBuilder.DropColumn(
                name: "SodiumMilligrams",
                table: "MealLogs");

            migrationBuilder.DropColumn(
                name: "SugarGrams",
                table: "MealLogs");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalAgent.Migrations
{
    /// <inheritdoc />
    public partial class ExpandMealLogNutrients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CalciumMilligrams",
                table: "MealLogs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "IronMilligrams",
                table: "MealLogs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MagnesiumMilligrams",
                table: "MealLogs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SaturatedFatGrams",
                table: "MealLogs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServingSize",
                table: "MealLogs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VitaminAMicrograms",
                table: "MealLogs",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalciumMilligrams",
                table: "MealLogs");

            migrationBuilder.DropColumn(
                name: "IronMilligrams",
                table: "MealLogs");

            migrationBuilder.DropColumn(
                name: "MagnesiumMilligrams",
                table: "MealLogs");

            migrationBuilder.DropColumn(
                name: "SaturatedFatGrams",
                table: "MealLogs");

            migrationBuilder.DropColumn(
                name: "ServingSize",
                table: "MealLogs");

            migrationBuilder.DropColumn(
                name: "VitaminAMicrograms",
                table: "MealLogs");
        }
    }
}

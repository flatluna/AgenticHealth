using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalAgent.Migrations
{
    /// <inheritdoc />
    public partial class AddFoodItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FoodItemId",
                table: "MealLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FoodItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ServingSize = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Calories = table.Column<double>(type: "float", nullable: true),
                    ProteinGrams = table.Column<double>(type: "float", nullable: true),
                    CarbsGrams = table.Column<double>(type: "float", nullable: true),
                    FatGrams = table.Column<double>(type: "float", nullable: true),
                    SaturatedFatGrams = table.Column<double>(type: "float", nullable: true),
                    SugarGrams = table.Column<double>(type: "float", nullable: true),
                    FiberGrams = table.Column<double>(type: "float", nullable: true),
                    SodiumMilligrams = table.Column<double>(type: "float", nullable: true),
                    PotassiumMilligrams = table.Column<double>(type: "float", nullable: true),
                    CalciumMilligrams = table.Column<double>(type: "float", nullable: true),
                    IronMilligrams = table.Column<double>(type: "float", nullable: true),
                    MagnesiumMilligrams = table.Column<double>(type: "float", nullable: true),
                    VitaminAMicrograms = table.Column<double>(type: "float", nullable: true),
                    IngredientsText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MatchKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TimesLogged = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MealLogs_FoodItemId",
                table: "MealLogs",
                column: "FoodItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FoodItems_MatchKey",
                table: "FoodItems",
                column: "MatchKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MealLogs_FoodItems_FoodItemId",
                table: "MealLogs",
                column: "FoodItemId",
                principalTable: "FoodItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MealLogs_FoodItems_FoodItemId",
                table: "MealLogs");

            migrationBuilder.DropTable(
                name: "FoodItems");

            migrationBuilder.DropIndex(
                name: "IX_MealLogs_FoodItemId",
                table: "MealLogs");

            migrationBuilder.DropColumn(
                name: "FoodItemId",
                table: "MealLogs");
        }
    }
}

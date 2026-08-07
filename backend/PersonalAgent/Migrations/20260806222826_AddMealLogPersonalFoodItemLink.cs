using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalAgent.Migrations
{
    /// <inheritdoc />
    public partial class AddMealLogPersonalFoodItemLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PersonalFoodItemId",
                table: "MealLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealLogs_PersonalFoodItemId",
                table: "MealLogs",
                column: "PersonalFoodItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_MealLogs_PersonalFoodItems_PersonalFoodItemId",
                table: "MealLogs",
                column: "PersonalFoodItemId",
                principalTable: "PersonalFoodItems",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MealLogs_PersonalFoodItems_PersonalFoodItemId",
                table: "MealLogs");

            migrationBuilder.DropIndex(
                name: "IX_MealLogs_PersonalFoodItemId",
                table: "MealLogs");

            migrationBuilder.DropColumn(
                name: "PersonalFoodItemId",
                table: "MealLogs");
        }
    }
}

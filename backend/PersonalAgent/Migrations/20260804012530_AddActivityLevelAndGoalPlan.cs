using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalAgent.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityLevelAndGoalPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActivityLevel",
                table: "People",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GoalPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    WeightKg = table.Column<double>(type: "float", nullable: false),
                    HeightCm = table.Column<double>(type: "float", nullable: false),
                    Bmi = table.Column<double>(type: "float", nullable: false),
                    ActivityLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GoalsText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecommendationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoalPlans_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoalPlans_PersonId",
                table: "GoalPlans",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoalPlans");

            migrationBuilder.DropColumn(
                name: "ActivityLevel",
                table: "People");
        }
    }
}

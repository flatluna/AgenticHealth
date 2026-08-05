using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalAgent.Migrations
{
    /// <inheritdoc />
    public partial class AddGoalPlanCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoalPlanCheckIns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GoalPlanId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    CheckInDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StepsWalked = table.Column<int>(type: "int", nullable: true),
                    FollowedNutrition = table.Column<bool>(type: "bit", nullable: false),
                    FollowedExercise = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalPlanCheckIns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoalPlanCheckIns_GoalPlans_GoalPlanId",
                        column: x => x.GoalPlanId,
                        principalTable: "GoalPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GoalPlanCheckIns_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoalPlanCheckIns_GoalPlanId_CheckInDate",
                table: "GoalPlanCheckIns",
                columns: new[] { "GoalPlanId", "CheckInDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoalPlanCheckIns_PersonId",
                table: "GoalPlanCheckIns",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoalPlanCheckIns");
        }
    }
}

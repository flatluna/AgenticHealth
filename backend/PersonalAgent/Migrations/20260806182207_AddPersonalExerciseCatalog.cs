using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalAgent.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalExerciseCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PersonalExerciseId",
                table: "ExerciseLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PersonalExercises",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    CaloriesBurned = table.Column<double>(type: "float", nullable: true),
                    TimesLogged = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalExercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonalExercises_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseLogs_PersonalExerciseId",
                table: "ExerciseLogs",
                column: "PersonalExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalExercises_PersonId_NormalizedName",
                table: "PersonalExercises",
                columns: new[] { "PersonId", "NormalizedName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ExerciseLogs_PersonalExercises_PersonalExerciseId",
                table: "ExerciseLogs",
                column: "PersonalExerciseId",
                principalTable: "PersonalExercises",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseLogs_PersonalExercises_PersonalExerciseId",
                table: "ExerciseLogs");

            migrationBuilder.DropTable(
                name: "PersonalExercises");

            migrationBuilder.DropIndex(
                name: "IX_ExerciseLogs_PersonalExerciseId",
                table: "ExerciseLogs");

            migrationBuilder.DropColumn(
                name: "PersonalExerciseId",
                table: "ExerciseLogs");
        }
    }
}

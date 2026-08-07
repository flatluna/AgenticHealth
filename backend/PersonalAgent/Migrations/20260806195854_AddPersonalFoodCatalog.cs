using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalAgent.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalFoodCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonalFoodItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(450)", nullable: false),
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
                    TimesLogged = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalFoodItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonalFoodItems_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalFoodItems_PersonId_NormalizedName",
                table: "PersonalFoodItems",
                columns: new[] { "PersonId", "NormalizedName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonalFoodItems");
        }
    }
}

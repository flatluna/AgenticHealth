using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalAgent.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonAzureObjectId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AzureObjectId",
                table: "People",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_AzureObjectId",
                table: "People",
                column: "AzureObjectId",
                unique: true,
                filter: "[AzureObjectId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_People_AzureObjectId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "AzureObjectId",
                table: "People");
        }
    }
}

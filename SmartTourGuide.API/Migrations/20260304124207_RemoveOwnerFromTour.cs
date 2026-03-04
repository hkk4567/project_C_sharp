using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourGuide.API.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOwnerFromTour : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tours_Users_OwnerId",
                table: "Tours");

            migrationBuilder.DropIndex(
                name: "IX_Tours_OwnerId",
                table: "Tours");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Tours");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OwnerId",
                table: "Tours",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Tours_OwnerId",
                table: "Tours",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tours_Users_OwnerId",
                table: "Tours",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

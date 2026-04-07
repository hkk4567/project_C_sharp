using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourGuide.API.Migrations
{
    public partial class RemoveUserIdFromUserLocationLogs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserLocationLogs_Users_UserId",
                table: "UserLocationLogs");

            migrationBuilder.DropIndex(
                name: "IX_UserLocationLogs_UserId",
                table: "UserLocationLogs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "UserLocationLogs");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "UserLocationLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserLocationLogs_UserId",
                table: "UserLocationLogs",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserLocationLogs_Users_UserId",
                table: "UserLocationLogs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
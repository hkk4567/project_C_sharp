using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourGuide.API.Migrations
{
    [Migration("20260407100000_AddDeviceIdToUserLocationLogs")]
    public partial class AddDeviceIdToUserLocationLogs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceId",
                table: "UserLocationLogs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "UserLocationLogs");
        }
    }
}
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTourGuide.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePoiListenLogDeviceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UserId",
                table: "PoiListenLogs");

            migrationBuilder.AddColumn<string>(
                name: "DeviceId",
                table: "PoiListenLogs",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "PoiListenLogs");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "PoiListenLogs",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}

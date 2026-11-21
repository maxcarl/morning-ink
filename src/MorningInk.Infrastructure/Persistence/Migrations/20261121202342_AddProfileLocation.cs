using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningInk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Location_District",
                table: "Profiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Location_Latitude",
                table: "Profiles",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Location_Longitude",
                table: "Profiles",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location_Name",
                table: "Profiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location_Query",
                table: "Profiles",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Location_District",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "Location_Latitude",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "Location_Longitude",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "Location_Name",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "Location_Query",
                table: "Profiles");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningInk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalNewsSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Sources",
                type: "TEXT",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 2048);

            migrationBuilder.AddColumn<string>(
                name: "Location_District",
                table: "Sources",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Location_Latitude",
                table: "Sources",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Location_Longitude",
                table: "Sources",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location_Name",
                table: "Sources",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location_Query",
                table: "Sources",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Place",
                table: "Sources",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Location_District",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "Location_Latitude",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "Location_Longitude",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "Location_Name",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "Location_Query",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "Place",
                table: "Sources");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Sources",
                type: "TEXT",
                maxLength: 2048,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 2048,
                oldNullable: true);
        }
    }
}

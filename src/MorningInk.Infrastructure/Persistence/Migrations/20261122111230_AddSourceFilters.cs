using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningInk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExcludeKeywords",
                table: "Sources",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "IncludeKeywords",
                table: "Sources",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "MinWords",
                table: "Sources",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SkipLiveTickers",
                table: "Sources",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcludeKeywords",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "IncludeKeywords",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "MinWords",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "SkipLiveTickers",
                table: "Sources");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningInk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "EmptySince",
                table: "Sources",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FailingSince",
                table: "Sources",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastProblem",
                table: "Sources",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastProblemArguments",
                table: "Sources",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]"); // an empty list; "" is no JSON

            migrationBuilder.AddColumn<string>(
                name: "LastProblemReason",
                table: "Sources",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmptySince",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "FailingSince",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "LastProblem",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "LastProblemArguments",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "LastProblemReason",
                table: "Sources");
        }
    }
}

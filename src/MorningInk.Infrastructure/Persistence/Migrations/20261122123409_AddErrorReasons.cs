using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningInk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddErrorReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorArguments",
                table: "Issues",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "ErrorReason",
                table: "Issues",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErrorArguments",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "ErrorReason",
                table: "Issues");
        }
    }
}

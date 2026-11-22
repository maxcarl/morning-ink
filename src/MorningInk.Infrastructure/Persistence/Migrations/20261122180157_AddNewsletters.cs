using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningInk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNewsletters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LoadImages",
                table: "Sources",
                type: "INTEGER",
                nullable: false,
                defaultValue: true); // existing sources keep loading their images

            migrationBuilder.AddColumn<string>(
                name: "Sender",
                table: "Sources",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LoadImages",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "Sender",
                table: "Sources");
        }
    }
}

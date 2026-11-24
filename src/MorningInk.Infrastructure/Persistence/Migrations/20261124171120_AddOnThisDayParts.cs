using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningInk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnThisDayParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowAnniversaries",
                table: "Section",
                type: "INTEGER",
                nullable: false,
                defaultValue: true); // existing "on this day" sections keep their anniversaries

            migrationBuilder.AddColumn<bool>(
                name: "ShowFeaturedArticle",
                table: "Section",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowAnniversaries",
                table: "Section");

            migrationBuilder.DropColumn(
                name: "ShowFeaturedArticle",
                table: "Section");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningInk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHeadlinesAndInterestsAi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HeadlinesPrompt",
                table: "AiSettings",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InterestsPrompt",
                table: "AiSettings",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeutralHeadlines",
                table: "AiSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeadlinesPrompt",
                table: "AiSettings");

            migrationBuilder.DropColumn(
                name: "InterestsPrompt",
                table: "AiSettings");

            migrationBuilder.DropColumn(
                name: "NeutralHeadlines",
                table: "AiSettings");
        }
    }
}

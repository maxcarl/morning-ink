using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningInk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyLimit",
                table: "AiSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SummaryPrompt",
                table: "AiSettings",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsLocal = table.Column<bool>(type: "INTEGER", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ArticleTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ArticleUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Characters = table.Column<int>(type: "INTEGER", nullable: false),
                    Succeeded = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCalls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiCalls_At",
                table: "AiCalls",
                column: "At");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiCalls");

            migrationBuilder.DropColumn(
                name: "DailyLimit",
                table: "AiSettings");

            migrationBuilder.DropColumn(
                name: "SummaryPrompt",
                table: "AiSettings");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningInk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFreshRss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MarkReadAfterDelivery",
                table: "Sources",
                type: "INTEGER",
                nullable: false,
                defaultValue: true); // like new sources; only FreshRSS sources use it

            migrationBuilder.AddColumn<string>(
                name: "Stream",
                table: "Sources",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "IssueEntry",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "IssueEntry",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MarkReadAfterDelivery",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "Stream",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "IssueEntry");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "IssueEntry");
        }
    }
}

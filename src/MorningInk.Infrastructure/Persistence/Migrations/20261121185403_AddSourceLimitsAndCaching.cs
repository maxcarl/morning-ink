using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningInk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceLimitsAndCaching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ETag",
                table: "Sources",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModified",
                table: "Sources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "MaxAge",
                table: "Sources",
                type: "TEXT",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<int>(
                name: "MaxArticles",
                table: "Sources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ETag",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "MaxAge",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "MaxArticles",
                table: "Sources");
        }
    }
}

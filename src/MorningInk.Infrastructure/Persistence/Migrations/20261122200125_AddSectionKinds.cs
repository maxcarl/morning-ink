using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningInk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSectionKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                table: "Section",
                type: "INTEGER",
                nullable: false,
                defaultValue: true); // every section so far was in the issue

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Section",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Articles"); // every section so far held articles

            migrationBuilder.AddColumn<string>(
                name: "OverviewPrompt",
                table: "AiSettings",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            // Every edition gets its weather as a section, first, so it stays on the title page as before and can now
            // be moved or switched off. Named in the owner's language; ids in the form EF Core stores a Guid in.
            migrationBuilder.Sql("""
                INSERT INTO "Section" ("Id", "ProfileId", "Name", "Position", "MaxArticles", "Kind", "Enabled")
                SELECT upper(hex(randomblob(4))) || '-' || upper(hex(randomblob(2))) || '-' || upper(hex(randomblob(2))) || '-'
                           || upper(hex(randomblob(2))) || '-' || upper(hex(randomblob(6))),
                       p."Id",
                       CASE u."Language" WHEN 'de' THEN 'Wetter' ELSE 'Weather' END,
                       0, NULL, 'Weather', 1
                FROM "Profiles" p JOIN "Users" u ON u."Id" = p."UserId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DELETE FROM "Section" WHERE "Kind" <> 'Articles';""");

            migrationBuilder.DropColumn(
                name: "Enabled",
                table: "Section");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Section");

            migrationBuilder.DropColumn(
                name: "OverviewPrompt",
                table: "AiSettings");
        }
    }
}

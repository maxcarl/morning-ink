using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningInk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnThisDaySection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every edition gets "on this day" last and switched off, like a new one: there to switch on, in nobody's
            // issue unasked. Named in the owner's language; ids in the form EF Core stores a Guid in.
            migrationBuilder.Sql("""
                INSERT INTO "Section" ("Id", "ProfileId", "Name", "Position", "MaxArticles", "Kind", "Enabled")
                SELECT upper(hex(randomblob(4))) || '-' || upper(hex(randomblob(2))) || '-' || upper(hex(randomblob(2))) || '-'
                           || upper(hex(randomblob(2))) || '-' || upper(hex(randomblob(6))),
                       p."Id",
                       CASE u."Language" WHEN 'de' THEN 'An diesem Tag' ELSE 'On this day' END,
                       COALESCE((SELECT max(s."Position") FROM "Section" s WHERE s."ProfileId" = p."Id"), 0) + 1,
                       NULL, 'OnThisDay', 0
                FROM "Profiles" p JOIN "Users" u ON u."Id" = p."UserId"
                WHERE NOT EXISTS (SELECT 1 FROM "Section" s WHERE s."ProfileId" = p."Id" AND s."Kind" = 'OnThisDay');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DELETE FROM "Section" WHERE "Kind" = 'OnThisDay';""");
        }
    }
}

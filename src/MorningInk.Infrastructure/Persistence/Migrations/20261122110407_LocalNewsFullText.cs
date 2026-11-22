using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningInk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocalNewsFullText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Local news now link straight to the publisher (Bing News), so they get full text like feeds.
            migrationBuilder.Sql("UPDATE Sources SET FetchFullText = 1 WHERE Kind = 'Local';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Sources SET FetchFullText = 0 WHERE Kind = 'Local';");
        }
    }
}

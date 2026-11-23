using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Echolines.Server.Migrations
{
    /// <inheritdoc />
    public partial class LinePagedPiece : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PagedPiece",
                table: "lines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_lines_Lang_NpcName_Paged",
                table: "lines",
                columns: new[] { "Lang", "NpcName", "Paged" });

            // Backfill for the lines that arrived before the column existed. Same rule as
            // PagedPieceMarker (hyphen and blank dropped on both sides); run once, the marker keeps
            // it current from here on. Postgres only, like every migration in this project.
            migrationBuilder.Sql("""
                UPDATE lines AS l SET "PagedPiece" = TRUE
                WHERE NOT l."Paged"
                  AND EXISTS (
                      SELECT 1 FROM lines AS w
                      WHERE w."Paged"
                        AND w."Lang" = l."Lang"
                        AND w."NpcName" = l."NpcName"
                        AND strpos(replace(replace(w."Text", '-', ''), ' ', ''),
                                   replace(replace(l."Text", '-', ''), ' ', '')) > 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_lines_Lang_NpcName_Paged",
                table: "lines");

            migrationBuilder.DropColumn(
                name: "PagedPiece",
                table: "lines");
        }
    }
}

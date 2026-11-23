using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Echolines.Server.Migrations
{
    /// <inheritdoc />
    public partial class LinePagedFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Paged",
                table: "pending_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Paged",
                table: "lines",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Paged",
                table: "pending_lines");

            migrationBuilder.DropColumn(
                name: "Paged",
                table: "lines");
        }
    }
}

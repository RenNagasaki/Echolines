using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Echolines.Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Lang = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    NpcName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Gender = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Race = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    NpcBaseId = table.Column<long>(type: "bigint", nullable: true),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    TextHash = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PromotedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceCount = table.Column<int>(type: "integer", nullable: false),
                    SeenLive = table.Column<bool>(type: "boolean", nullable: false),
                    SeenHarvest = table.Column<bool>(type: "boolean", nullable: false),
                    OriginOperator = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pending_lines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Lang = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    NpcName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Gender = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Race = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    NpcBaseId = table.Column<long>(type: "bigint", nullable: true),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    TextHash = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HitCount = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_lines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pending_votes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PendingId = table.Column<int>(type: "integer", nullable: false),
                    VoterKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Origin = table.Column<int>(type: "integer", nullable: false),
                    SeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pending_votes_pending_lines_PendingId",
                        column: x => x.PendingId,
                        principalTable: "pending_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lines_Lang_NpcName",
                table: "lines",
                columns: new[] { "Lang", "NpcName" });

            migrationBuilder.CreateIndex(
                name: "IX_lines_Lang_NpcName_TextHash",
                table: "lines",
                columns: new[] { "Lang", "NpcName", "TextHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lines_LastSeenUtc",
                table: "lines",
                column: "LastSeenUtc");

            migrationBuilder.CreateIndex(
                name: "IX_lines_SeenLive_SeenHarvest",
                table: "lines",
                columns: new[] { "SeenLive", "SeenHarvest" });

            migrationBuilder.CreateIndex(
                name: "IX_lines_TextHash",
                table: "lines",
                column: "TextHash");

            migrationBuilder.CreateIndex(
                name: "IX_pending_lines_Lang_NpcName",
                table: "pending_lines",
                columns: new[] { "Lang", "NpcName" });

            migrationBuilder.CreateIndex(
                name: "IX_pending_lines_Lang_NpcName_TextHash",
                table: "pending_lines",
                columns: new[] { "Lang", "NpcName", "TextHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pending_lines_State_LastSeenUtc",
                table: "pending_lines",
                columns: new[] { "State", "LastSeenUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_pending_lines_TextHash",
                table: "pending_lines",
                column: "TextHash");

            migrationBuilder.CreateIndex(
                name: "IX_pending_votes_PendingId_VoterKey",
                table: "pending_votes",
                columns: new[] { "PendingId", "VoterKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lines");

            migrationBuilder.DropTable(
                name: "pending_votes");

            migrationBuilder.DropTable(
                name: "pending_lines");
        }
    }
}

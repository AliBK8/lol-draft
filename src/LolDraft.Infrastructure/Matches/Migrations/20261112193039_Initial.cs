using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LolDraft.Infrastructure.Matches.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 22, nullable: false),
                    Lobby = table.Column<string>(type: "TEXT", maxLength: 22, nullable: false),
                    GameNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    BlueTeam = table.Column<string>(type: "TEXT", maxLength: 1, nullable: false),
                    Seed = table.Column<long>(type: "INTEGER", nullable: false),
                    EngineVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PlayedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ReplayVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Replay = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_PlayedAt",
                table: "Matches",
                column: "PlayedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Matches");
        }
    }
}

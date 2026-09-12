using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round10 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PostItems",
                table: "PostItems");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "PostItems",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "PostItems",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Confirmed",
                table: "PostItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "PostItems",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "PostItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "PostItems",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Url",
                table: "PostItems",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "X",
                table: "PostItems",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Y",
                table: "PostItems",
                type: "REAL",
                nullable: true);

            // Hand-written. The Round 9 rows were keyed on (PostId, Name) and have no id, source or order. Each gets a fresh
            // random id (a v4 GUID in the text form EF stores), the stylist as its source (nothing else could have written
            // it) and its position in insertion order, before SQLite rebuilds the table around the new key at the end of
            // this migration; without this the rebuild copies the one zero id into every row and stops at the second.
            migrationBuilder.Sql("""
                UPDATE "PostItems" SET
                    "Id" = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-'
                           || substr('89ab', (random() & 3) + 1, 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6))),
                    "Source" = 'Stylist',
                    "Position" = (SELECT COUNT(*) FROM "PostItems" AS earlier WHERE earlier."PostId" = "PostItems"."PostId" AND earlier.rowid < "PostItems".rowid)
                """);

            migrationBuilder.AddColumn<int>(
                name: "Rank",
                table: "Notifications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PostItems",
                table: "PostItems",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "BoardExclusions",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardExclusions", x => x.PostId);
                    table.ForeignKey(
                        name: "FK_BoardExclusions_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BoardExclusions_Users_ByUserId",
                        column: x => x.ByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Counters",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Counters", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "WeeklyWinners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WeekStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Board = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Fires = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyWinners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeeklyWinners_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WeeklyWinners_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostItems_Brand",
                table: "PostItems",
                column: "Brand");

            migrationBuilder.CreateIndex(
                name: "IX_PostItems_Category",
                table: "PostItems",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_PostItems_PostId_Position",
                table: "PostItems",
                columns: new[] { "PostId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Fires_CreatedAt",
                table: "Fires",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BoardExclusions_ByUserId",
                table: "BoardExclusions",
                column: "ByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyWinners_PostId",
                table: "WeeklyWinners",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyWinners_UserId_WeekStart",
                table: "WeeklyWinners",
                columns: new[] { "UserId", "WeekStart" });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyWinners_WeekStart_Board_Rank",
                table: "WeeklyWinners",
                columns: new[] { "WeekStart", "Board", "Rank" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BoardExclusions");

            migrationBuilder.DropTable(
                name: "Counters");

            migrationBuilder.DropTable(
                name: "WeeklyWinners");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PostItems",
                table: "PostItems");

            migrationBuilder.DropIndex(
                name: "IX_PostItems_Brand",
                table: "PostItems");

            migrationBuilder.DropIndex(
                name: "IX_PostItems_Category",
                table: "PostItems");

            migrationBuilder.DropIndex(
                name: "IX_PostItems_PostId_Position",
                table: "PostItems");

            migrationBuilder.DropIndex(
                name: "IX_Fires_CreatedAt",
                table: "Fires");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "PostItems");

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "PostItems");

            migrationBuilder.DropColumn(
                name: "Confirmed",
                table: "PostItems");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "PostItems");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "PostItems");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "PostItems");

            migrationBuilder.DropColumn(
                name: "Url",
                table: "PostItems");

            migrationBuilder.DropColumn(
                name: "X",
                table: "PostItems");

            migrationBuilder.DropColumn(
                name: "Y",
                table: "PostItems");

            migrationBuilder.DropColumn(
                name: "Rank",
                table: "Notifications");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PostItems",
                table: "PostItems",
                columns: new[] { "PostId", "Name" });
        }
    }
}

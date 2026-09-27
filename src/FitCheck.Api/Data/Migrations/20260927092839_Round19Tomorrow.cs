using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round19Tomorrow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SuggestionId",
                table: "Checks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Suggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Occasion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Style = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Intent = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    When = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    ForDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Seq = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Sentence = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    SentenceTemplated = table.Column<bool>(type: "INTEGER", nullable: false),
                    Gap = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    PiecesOffered = table.Column<int>(type: "INTEGER", nullable: false),
                    InventedRefs = table.Column<int>(type: "INTEGER", nullable: false),
                    Reuses = table.Column<int>(type: "INTEGER", nullable: false),
                    TasteUsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    WeatherUsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    WeatherTempMaxC = table.Column<double>(type: "REAL", nullable: true),
                    WeatherTempMinC = table.Column<double>(type: "REAL", nullable: true),
                    WeatherPrecipChance = table.Column<int>(type: "INTEGER", nullable: true),
                    WeatherCode = table.Column<int>(type: "INTEGER", nullable: true),
                    PromptVersion = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Useful = table.Column<bool>(type: "INTEGER", nullable: true),
                    UsefulAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UsefulNote = table.Column<string>(type: "TEXT", nullable: true),
                    UsefulReason = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    WornCheckId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Suggestions_Checks_WornCheckId",
                        column: x => x.WornCheckId,
                        principalTable: "Checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Suggestions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SuggestionPieces",
                columns: table => new
                {
                    SuggestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PhotoCheckId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuggestionPieces", x => new { x.SuggestionId, x.Position });
                    table.ForeignKey(
                        name: "FK_SuggestionPieces_Checks_PhotoCheckId",
                        column: x => x.PhotoCheckId,
                        principalTable: "Checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SuggestionPieces_Suggestions_SuggestionId",
                        column: x => x.SuggestionId,
                        principalTable: "Suggestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SuggestionPieces_WardrobeItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "WardrobeItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Checks_SuggestionId",
                table: "Checks",
                column: "SuggestionId");

            migrationBuilder.CreateIndex(
                name: "IX_SuggestionPieces_ItemId",
                table: "SuggestionPieces",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SuggestionPieces_PhotoCheckId",
                table: "SuggestionPieces",
                column: "PhotoCheckId");

            migrationBuilder.CreateIndex(
                name: "IX_Suggestions_CreatedAt",
                table: "Suggestions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Suggestions_UserId_CreatedAt",
                table: "Suggestions",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Suggestions_UserId_Occasion_ForDate_CreatedAt",
                table: "Suggestions",
                columns: new[] { "UserId", "Occasion", "ForDate", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Suggestions_WornCheckId",
                table: "Suggestions",
                column: "WornCheckId");

            migrationBuilder.AddForeignKey(
                name: "FK_Checks_Suggestions_SuggestionId",
                table: "Checks",
                column: "SuggestionId",
                principalTable: "Suggestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Checks_Suggestions_SuggestionId",
                table: "Checks");

            migrationBuilder.DropTable(
                name: "SuggestionPieces");

            migrationBuilder.DropTable(
                name: "Suggestions");

            migrationBuilder.DropIndex(
                name: "IX_Checks_SuggestionId",
                table: "Checks");

            migrationBuilder.DropColumn(
                name: "SuggestionId",
                table: "Checks");
        }
    }
}

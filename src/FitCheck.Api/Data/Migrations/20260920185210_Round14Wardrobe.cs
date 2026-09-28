using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round14Wardrobe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WardrobeItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WardrobeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WardrobeItems_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WardrobeSettings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToStylist = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WardrobeSettings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_WardrobeSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WardrobeAppearances",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WornAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WardrobeAppearances", x => new { x.ItemId, x.CheckId });
                    table.ForeignKey(
                        name: "FK_WardrobeAppearances_Checks_CheckId",
                        column: x => x.CheckId,
                        principalTable: "Checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WardrobeAppearances_WardrobeItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "WardrobeItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WardrobeAppearances_CheckId",
                table: "WardrobeAppearances",
                column: "CheckId");

            migrationBuilder.CreateIndex(
                name: "IX_WardrobeItems_UserId_LastSeenAt",
                table: "WardrobeItems",
                columns: new[] { "UserId", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WardrobeItems_UserId_NameKey",
                table: "WardrobeItems",
                columns: new[] { "UserId", "NameKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WardrobeAppearances");

            migrationBuilder.DropTable(
                name: "WardrobeSettings");

            migrationBuilder.DropTable(
                name: "WardrobeItems");
        }
    }
}

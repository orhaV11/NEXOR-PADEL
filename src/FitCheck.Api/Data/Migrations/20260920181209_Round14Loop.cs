using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round14Loop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UsefulReason",
                table: "Checks",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CheckLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BeforeCheckId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AfterCheckId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Preferred = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    PreferredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckLinks_Checks_AfterCheckId",
                        column: x => x.AfterCheckId,
                        principalTable: "Checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CheckLinks_Checks_BeforeCheckId",
                        column: x => x.BeforeCheckId,
                        principalTable: "Checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CheckLinks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TasteSettings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Learning = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TasteSettings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_TasteSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckLinks_AfterCheckId",
                table: "CheckLinks",
                column: "AfterCheckId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckLinks_BeforeCheckId",
                table: "CheckLinks",
                column: "BeforeCheckId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckLinks_UserId_CreatedAt",
                table: "CheckLinks",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckLinks");

            migrationBuilder.DropTable(
                name: "TasteSettings");

            migrationBuilder.DropColumn(
                name: "UsefulReason",
                table: "Checks");
        }
    }
}

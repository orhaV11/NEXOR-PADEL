using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round20Wedge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BoardExcludedAt",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RenewalRecapUntil",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Users",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TomorrowPushOn",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CheckId",
                table: "Notifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OccasionKind",
                table: "Comparisons",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Everyday");

            migrationBuilder.AddColumn<string>(
                name: "Style",
                table: "Comparisons",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            // Round 20: rows from before the two questions carry the split of their one word, as Round14Check did for checks.
            // Formal, added later, has its own word too.
            migrationBuilder.Sql("""
                UPDATE "Comparisons" SET
                  "OccasionKind" = CASE "Intent"
                    WHEN 'Date' THEN 'Date'
                    WHEN 'Office' THEN 'Office'
                    WHEN 'Party' THEN 'Party'
                    WHEN 'Sport' THEN 'Sport'
                    WHEN 'Formal' THEN 'Formal'
                    ELSE 'Everyday'
                  END,
                  "Style" = CASE "Intent"
                    WHEN 'Streetwear' THEN 'Streetwear'
                    WHEN 'OldMoney' THEN 'OldMoney'
                    WHEN 'Minimal' THEN 'Minimal'
                    ELSE NULL
                  END;
                """);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Checks",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StripeEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TomorrowPushes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TomorrowPushes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TomorrowPushes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StripeEvents_ReceivedAt",
                table: "StripeEvents",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TomorrowPushes_SentAt",
                table: "TomorrowPushes",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_TomorrowPushes_UserId_Day",
                table: "TomorrowPushes",
                columns: new[] { "UserId", "Day" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StripeEvents");

            migrationBuilder.DropTable(
                name: "TomorrowPushes");

            migrationBuilder.DropColumn(
                name: "BoardExcludedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RenewalRecapUntil",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TomorrowPushOn",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CheckId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "OccasionKind",
                table: "Comparisons");

            migrationBuilder.DropColumn(
                name: "Style",
                table: "Comparisons");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Checks");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round16Affiliate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Commissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemClicks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Host = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Earning = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemClicks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_Host_ExternalId",
                table: "Commissions",
                columns: new[] { "Host", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_State_OccurredAt",
                table: "Commissions",
                columns: new[] { "State", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemClicks_CreatedAt",
                table: "ItemClicks",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ItemClicks_Host_CreatedAt",
                table: "ItemClicks",
                columns: new[] { "Host", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemClicks_PostId_CreatedAt",
                table: "ItemClicks",
                columns: new[] { "PostId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Commissions");

            migrationBuilder.DropTable(
                name: "ItemClicks");
        }
    }
}

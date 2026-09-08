using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round9 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingCustomerId",
                table: "Users",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BirthDate",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "Users",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ProUntil",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Verified",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "BeforePostId",
                table: "Posts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Checks",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAt",
                table: "Checks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestToken",
                table: "Checks",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Comparisons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GuestToken = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Intent = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Occasion = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ImagePathA = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    ImagePathB = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    Winner = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FeedbackJson = table.Column<string>(type: "TEXT", nullable: true),
                    PromptVersion = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comparisons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comparisons_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostItems",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostItems", x => new { x.PostId, x.Name });
                    table.ForeignKey(
                        name: "FK_PostItems_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_BillingCustomerId",
                table: "Users",
                column: "BillingCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_BeforePostId",
                table: "Posts",
                column: "BeforePostId");

            migrationBuilder.CreateIndex(
                name: "IX_Checks_GuestToken",
                table: "Checks",
                column: "GuestToken");

            migrationBuilder.CreateIndex(
                name: "IX_Comparisons_GuestToken",
                table: "Comparisons",
                column: "GuestToken");

            migrationBuilder.CreateIndex(
                name: "IX_Comparisons_UserId_CreatedAt",
                table: "Comparisons",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PostItems_Name",
                table: "PostItems",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_Posts_Posts_BeforePostId",
                table: "Posts",
                column: "BeforePostId",
                principalTable: "Posts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Posts_Posts_BeforePostId",
                table: "Posts");

            migrationBuilder.DropTable(
                name: "Comparisons");

            migrationBuilder.DropTable(
                name: "PostItems");

            migrationBuilder.DropIndex(
                name: "IX_Users_BillingCustomerId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Posts_BeforePostId",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_Checks_GuestToken",
                table: "Checks");

            migrationBuilder.DropColumn(
                name: "BillingCustomerId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProUntil",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Verified",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BeforePostId",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                table: "Checks");

            migrationBuilder.DropColumn(
                name: "GuestToken",
                table: "Checks");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Checks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round13 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DigestOn",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "InvitedByUserId",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDigestAt",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Useful",
                table: "Checks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UsefulAt",
                table: "Checks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsefulNote",
                table: "Checks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DigestOn",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "InvitedByUserId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastDigestAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Useful",
                table: "Checks");

            migrationBuilder.DropColumn(
                name: "UsefulAt",
                table: "Checks");

            migrationBuilder.DropColumn(
                name: "UsefulNote",
                table: "Checks");
        }
    }
}

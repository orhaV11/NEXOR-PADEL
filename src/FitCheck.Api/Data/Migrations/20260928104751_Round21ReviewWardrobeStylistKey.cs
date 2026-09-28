using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round21ReviewWardrobeStylistKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The stylist's key a kept piece was kept under, which a rename never changes, so a renamed piece is still the
            // one a check names and never comes back under "Keep from an older look". Nullable with no default: a row
            // kept before it reads by its name as before and gets the key at its first rename.
            migrationBuilder.AddColumn<string>(
                name: "StylistKey",
                table: "WardrobeItems",
                type: "TEXT",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StylistKey",
                table: "WardrobeItems");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <summary>
    /// Round 14 — the occasion split, on the Checks table and nothing else. The one list that held two questions becomes
    /// two columns: OccasionKind (where the outfit is going, on every check) and Style (how it should read, NULL when the
    /// wearer asked for none, which is a first-class answer). Every stored check is split by its Intent, which stays
    /// exactly as it was: it is the one word a look, a board, a challenge and the feed filter speak.
    /// <para>
    /// Nothing is renamed and nothing is copied. The wearer's free line stays in the column it has been in since the
    /// first migration ("Occasion", now mapped to OutfitCheck.Note), because DatabaseSetup upgrades a pilot database made
    /// before migrations by matching the model's columns against the file's: a rename would leave such a file with an
    /// orphan column, or make 120 characters of someone's words have to parse as an enum. Down() drops the two new
    /// columns and leaves every row as it was found.
    /// </para>
    /// </summary>
    public partial class Round14Check : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OccasionKind",
                table: "Checks",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Everyday");

            migrationBuilder.AddColumn<string>(
                name: "Style",
                table: "Checks",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            // The split, row by row: Date, Office, Party and Sport were occasions with no style asked for; Casual was
            // Everyday; Streetwear, OldMoney and Minimal were styles worn Everyday. An Intent this does not know (there
            // is none) lands on Everyday with no style, which is the "judge it on its own terms" the app already does.
            migrationBuilder.Sql("""
                UPDATE "Checks" SET
                  "OccasionKind" = CASE "Intent"
                    WHEN 'Date' THEN 'Date'
                    WHEN 'Office' THEN 'Office'
                    WHEN 'Party' THEN 'Party'
                    WHEN 'Sport' THEN 'Sport'
                    ELSE 'Everyday'
                  END,
                  "Style" = CASE "Intent"
                    WHEN 'Streetwear' THEN 'Streetwear'
                    WHEN 'OldMoney' THEN 'OldMoney'
                    WHEN 'Minimal' THEN 'Minimal'
                    ELSE NULL
                  END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OccasionKind",
                table: "Checks");

            migrationBuilder.DropColumn(
                name: "Style",
                table: "Checks");
        }
    }
}

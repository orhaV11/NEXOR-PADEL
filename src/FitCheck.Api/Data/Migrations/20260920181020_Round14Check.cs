using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <summary>
    /// Round 14 — the occasion split, on the Checks table only. The one list that held both questions becomes two
    /// columns: Occasion (where it is going, on every check) and Style (how it should read, null when none was asked
    /// for). The free line the wearer used to type into a column called Occasion keeps every character and moves to
    /// Note, so nothing anyone wrote is lost. Intent stays exactly as it was — it is the one word a look, a board, a
    /// challenge and the feed filter speak — and is now derived from the pair.
    /// <para>
    /// The backfill is the split, row by row: Date, Office, Party and Sport are occasions with no style, Casual is
    /// Everyday, and Streetwear, OldMoney and Minimal were styles worn Everyday. Down() puts the file back the way it
    /// came: the two new columns go and Note becomes Occasion again, with the wearer's words still in it.
    /// </para>
    /// </summary>
    public partial class Round14Check : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The wearer's free line, renamed rather than rebuilt: no row is copied, no character is lost.
            migrationBuilder.RenameColumn(
                name: "Occasion",
                table: "Checks",
                newName: "Note");

            migrationBuilder.AddColumn<string>(
                name: "Occasion",
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

            // Every stored check, split. An Intent this does not know (there is none) lands on Everyday with no style,
            // which is the same "judge it on its own terms" the app has always done for a check with no style.
            migrationBuilder.Sql("""
                UPDATE "Checks" SET
                  "Occasion" = CASE "Intent"
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
                name: "Occasion",
                table: "Checks");

            migrationBuilder.DropColumn(
                name: "Style",
                table: "Checks");

            migrationBuilder.RenameColumn(
                name: "Note",
                table: "Checks",
                newName: "Occasion");
        }
    }
}

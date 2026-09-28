using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round21ReviewPushDefaultAndNudgeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The model now carries the default Round20Wedge wrote by hand (TomorrowPushOn on), so a pilot file upgraded from
            // the model gets it too. The column's DEFAULT is rewritten here (a Users rebuild on SQLite, as Round9's was for
            // Checks) because inserts now leave an "on" to the database default: a file whose column came in as DEFAULT 0 —
            // a pilot file upgraded by the Round 20 build — would otherwise give every new account the push off. Rows already
            // there keep what they hold; a switch somebody turned off is never turned back on.
            migrationBuilder.AlterColumn<bool>(
                name: "TomorrowPushOn",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER");

            // The try-tip nudge and the numbers page look a nudge up by type and check, with no account to start from.
            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Type_CheckId",
                table: "Notifications",
                columns: new[] { "Type", "CheckId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_Type_CheckId",
                table: "Notifications");

            migrationBuilder.AlterColumn<bool>(
                name: "TomorrowPushOn",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: true);
        }
    }
}

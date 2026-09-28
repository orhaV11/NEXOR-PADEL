using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round21ReviewRenewal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // What the renewal recap needs to know and ProUntil cannot say: when Stripe charges next (ProUntil carries the
            // slack, a Checkout's 35 or 368 days and any gift under it) and whether it will charge at all (a cancel at the
            // period end, a trial with no card, a declined renewal). Both nullable with no default: an existing subscriber
            // reads as renewing on ProUntil's date, as before, until Stripe's next event names both.
            migrationBuilder.AddColumn<DateTime>(
                name: "BillingPeriodEnd",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BillingRenews",
                table: "Users",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingPeriodEnd",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BillingRenews",
                table: "Users");
        }
    }
}

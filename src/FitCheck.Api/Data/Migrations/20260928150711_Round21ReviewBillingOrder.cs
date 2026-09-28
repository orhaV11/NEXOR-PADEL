using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitCheck.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round21ReviewBillingOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Review of Round 21: when Stripe created the subscription event the renewal fields were last taken from, so a
            // late or out-of-order one cannot move the next charge back or restore a renewal the person cancelled. Nullable,
            // no default: an existing row takes the next event as it comes, as before.
            migrationBuilder.AddColumn<DateTime>(
                name: "BillingNotedAt",
                table: "Users",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingNotedAt",
                table: "Users");
        }
    }
}

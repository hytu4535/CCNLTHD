using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StripeWebhooks.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stripe_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payment_intent_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    amount = table.Column<long>(type: "bigint", nullable: true),
                    currency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    event_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_events", x => x.event_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_events_stripe_event_id",
                table: "payment_events",
                column: "stripe_event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_events");

            migrationBuilder.DropTable(
                name: "processed_events");
        }
    }
}

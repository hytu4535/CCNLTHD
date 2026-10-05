using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StripeWebhooks.Api.Persistence;

#nullable disable

namespace StripeWebhooks.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261005000000_AddPaymentIntentToOrders")]
public partial class AddPaymentIntentToOrders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "payment_intent_id",
            table: "orders",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "payment_status",
            table: "orders",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Pending");

        migrationBuilder.CreateIndex(
            name: "IX_orders_payment_intent_id",
            table: "orders",
            column: "payment_intent_id",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_orders_payment_intent_id",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "payment_intent_id",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "payment_status",
            table: "orders");
    }
}

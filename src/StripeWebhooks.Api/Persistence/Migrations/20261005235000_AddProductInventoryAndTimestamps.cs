using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StripeWebhooks.Api.Persistence;

#nullable disable

namespace StripeWebhooks.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261005235000_AddProductInventoryAndTimestamps")]
public partial class AddProductInventoryAndTimestamps : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "stock_quantity",
            table: "products",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "created_at",
            table: "products",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "CURRENT_TIMESTAMP");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "updated_at",
            table: "products",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "stock_quantity", table: "products");
        migrationBuilder.DropColumn(name: "created_at", table: "products");
        migrationBuilder.DropColumn(name: "updated_at", table: "products");
    }
}

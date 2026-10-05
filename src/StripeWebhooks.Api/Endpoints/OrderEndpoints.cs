using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using StripeWebhooks.Api.Persistence;
using StripeWebhooks.Api.Persistence.Entities;

namespace StripeWebhooks.Api.Endpoints;

public sealed record CreateOrderRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [Range(1, 10000)]
    public int Quantity { get; init; }

    [Required, StringLength(128, MinimumLength = 1)]
    [RegularExpression(@"^pi_[A-Za-z0-9]+$", ErrorMessage = "PaymentIntentId must be a valid Stripe PaymentIntent ID.")]
    public required string PaymentIntentId { get; init; }
}

public sealed record UpdateOrderRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [Range(1, 10000)]
    public int Quantity { get; init; }
}

public sealed record OrderResponse(
    int Id,
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Total,
    string? PaymentIntentId,
    string PaymentStatus,
    DateTimeOffset CreatedAt);

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").AddEndpointFilter<ValidationFilter>();

        group.MapGet("", async (AppDbContext db, CancellationToken ct) =>
        {
            var orders = await db.Orders.AsNoTracking()
                .Include(order => order.Product)
                .OrderBy(order => order.Id)
                .Select(order => ToResponse(order))
                .ToListAsync(ct);

            return Results.Ok(orders);
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var order = await db.Orders.AsNoTracking()
                .Include(order => order.Product)
                .FirstOrDefaultAsync(order => order.Id == id, ct);

            return order is null ? Results.NotFound() : Results.Ok(ToResponse(order));
        });

        group.MapPost("", async (CreateOrderRequest request, AppDbContext db, CancellationToken ct) =>
        {
            var product = await db.Products.FindAsync([request.ProductId], ct);
            if (product is null)
                return Results.NotFound(new { error = "Product was not found." });

            var paymentIntentExists = await db.Orders
                .AnyAsync(order => order.PaymentIntentId == request.PaymentIntentId, ct);
            if (paymentIntentExists)
                return Results.Conflict(new { error = "This PaymentIntent is already linked to an order." });

            var order = new Order
            {
                ProductId = product.Id,
                Product = product,
                Quantity = request.Quantity,
                PaymentIntentId = request.PaymentIntentId
            };

            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/orders/{order.Id}", ToResponse(order));
        });

        group.MapPut("/{id:int}", async (int id, UpdateOrderRequest request, AppDbContext db, CancellationToken ct) =>
        {
            var order = await db.Orders.Include(item => item.Product)
                .FirstOrDefaultAsync(item => item.Id == id, ct);
            if (order is null)
                return Results.NotFound();

            var product = await db.Products.FindAsync([request.ProductId], ct);
            if (product is null)
                return Results.NotFound(new { error = "Product was not found." });

            order.ProductId = product.Id;
            order.Product = product;
            order.Quantity = request.Quantity;
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToResponse(order));
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var order = await db.Orders.FindAsync([id], ct);
            if (order is null)
                return Results.NotFound();

            db.Orders.Remove(order);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    private static OrderResponse ToResponse(Order order) => new(
        order.Id,
        order.ProductId,
        order.Product.Name,
        order.Product.Price,
        order.Quantity,
        order.Product.Price * order.Quantity,
        order.PaymentIntentId,
        order.PaymentStatus.ToString(),
        order.CreatedAt);
}
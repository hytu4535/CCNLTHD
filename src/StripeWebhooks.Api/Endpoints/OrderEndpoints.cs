using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using StripeWebhooks.Api.Persistence;
using StripeWebhooks.Api.Persistence.Entities;
using Stripe;

namespace StripeWebhooks.Api.Endpoints;

public sealed record CreateOrderRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [Range(1, 10000)]
    public int Quantity { get; init; }

    [Required, StringLength(128, MinimumLength = 1)]
    [RegularExpression(@"^pi_[A-Za-z0-9_]+$", ErrorMessage = "PaymentIntentId must be a valid Stripe PaymentIntent ID.")]
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

        group.MapPost("", async (
            CreateOrderRequest request,
            AppDbContext db,
            IConfiguration config,
            CancellationToken ct) =>
        {
            var product = await db.Products
                .FindAsync([request.ProductId], ct);

            if (product is null)
                return Results.NotFound(
                    new { error = "Product was not found." });

            // Lấy giá từ database, không lấy từ client.
            var total = product.Price * request.Quantity;

            // Stripe nhận số tiền theo đơn vị nhỏ nhất:
            // 14.50 USD = 1450 cents.
            var amountInCents = (long)Math.Round(
                total * 100m,
                MidpointRounding.AwayFromZero);

            if (amountInCents <= 0)
                return Results.BadRequest(
                    new { error = "Order amount must be positive." });

            // Tạo Order trước để có Order ID.
            var order = new Order
            {
                ProductId = product.Id,
                Product = product,
                Quantity = request.Quantity
            };

            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);

            try
            {
                var secretKey = config["Stripe:SecretKey"];

                if (string.IsNullOrWhiteSpace(secretKey))
                    throw new InvalidOperationException(
                        "Stripe:SecretKey is not configured.");

                var stripeClient = new StripeClient(secretKey);
                var paymentIntentService =
                    new PaymentIntentService(stripeClient);

                var options = new PaymentIntentCreateOptions
                {
                    Amount = amountInCents,
                    Currency = "usd",
                    PaymentMethodTypes = new List<string>
                    {
                        "card"
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["orderId"] = order.Id.ToString()
                    }
                };

                var paymentIntent =
                    await paymentIntentService.CreateAsync(
                        options,
                        cancellationToken: ct);

                // Stripe tạo ID; backend lưu ID vào Order.
                order.PaymentIntentId = paymentIntent.Id;
                await db.SaveChangesAsync(ct);

                return Results.Created(
                    $"/api/orders/{order.Id}",
                    new
                    {
                        orderId = order.Id,
                        paymentIntentId = paymentIntent.Id,
                        clientSecret = paymentIntent.ClientSecret,
                        amount = amountInCents,
                        currency = "usd",
                        paymentStatus = order.PaymentStatus.ToString()
                    });
            }
            catch (StripeException)
            {
                // Trong bản demo: không để đơn hàng tiếp tục được
                // thanh toán nếu tạo PaymentIntent thất bại.
                db.Orders.Remove(order);
                await db.SaveChangesAsync(ct);

                return Results.Problem(
                    "Could not create the Stripe PaymentIntent.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
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
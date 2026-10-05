using Microsoft.EntityFrameworkCore;
using StripeWebhooks.Api.DTOs;
using StripeWebhooks.Api.Models;
using StripeWebhooks.Api.Persistence;

namespace StripeWebhooks.Api.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products")
            .WithTags("Products")
            .AddEndpointFilter<ValidationFilter>();

        // GET /api/products
        group.MapGet("/", async (string? search, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Products.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(term));
            }

            var products = await query
                .OrderByDescending(p => p.Id)
                .Select(p => ToResponse(p))
                .ToListAsync(ct);

            return Results.Ok(products);
        });

        // GET /api/products/{id}
        group.MapGet("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var product = await db.Products.FindAsync([id], ct);
            if (product is null)
                return Results.NotFound(new { error = $"Product with ID {id} not found." });

            return Results.Ok(ToResponse(product));
        });

        // POST /api/products
        group.MapPost("/", async (CreateProductDto dto, AppDbContext db, CancellationToken ct) =>
        {
            if (dto.Id.HasValue && dto.Id.Value > 0)
            {
                var idExists = await db.Products.AnyAsync(p => p.Id == dto.Id.Value, ct);
                if (idExists)
                {
                    return Results.Conflict(new { message = $"Sản phẩm với ID {dto.Id.Value} đã tồn tại, không thể tạo mới!" });
                }
            }

            var trimmedName = dto.Name.Trim();
            var nameExists = await db.Products.AnyAsync(p => p.Name.ToLower() == trimmedName.ToLower(), ct);
            if (nameExists)
            {
                return Results.Conflict(new { message = "Sản phẩm với tên này đã tồn tại!" });
            }

            var product = new Product
            {
                Name = trimmedName,
                Price = dto.Price,
                StockQuantity = dto.StockQuantity,
                CreatedAt = DateTimeOffset.UtcNow
            };

            if (dto.Id.HasValue && dto.Id.Value > 0)
                product.Id = dto.Id.Value;

            db.Products.Add(product);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/products/{product.Id}", ToResponse(product));
        });

        // PUT /api/products/{id}
        group.MapPut("/{id:int}", async (int id, UpdateProductDto dto, AppDbContext db, CancellationToken ct) =>
        {
            var product = await db.Products.FindAsync([id], ct);
            if (product == null)
                return Results.NotFound(new { message = $"Không tìm thấy sản phẩm với ID = {id}" });

            var trimmedName = dto.Name.Trim();
            var nameExists = await db.Products.AnyAsync(p => p.Id != id && p.Name.ToLower() == trimmedName.ToLower(), ct);
            if (nameExists)
            {
                return Results.Conflict(new { message = "Tên sản phẩm này đã được sử dụng bởi sản phẩm khác!" });
            }

            product.Name = trimmedName;
            product.Price = dto.Price;
            product.StockQuantity = dto.StockQuantity;
            product.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);

            return Results.Ok(ToResponse(product));
        });

        // DELETE /api/products/{id}
        group.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var product = await db.Products.FindAsync([id], ct);
            if (product is null)
                return Results.NotFound(new { error = $"Product with ID {id} not found." });

            db.Products.Remove(product);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        return app;
    }

    private static ProductResponse ToResponse(Product product) => new(
        product.Id,
        product.Name,
        product.Price,
        product.StockQuantity,
        product.CreatedAt,
        product.UpdatedAt);
}
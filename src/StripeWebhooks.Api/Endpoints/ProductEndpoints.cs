using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using StripeWebhooks.Api.Persistence;
using StripeWebhooks.Api.Persistence.Entities;

namespace StripeWebhooks.Api.Endpoints;

public sealed record CreateProductRequest
{
    [Required, StringLength(150, MinimumLength = 1)]
    public required string Name { get; init; }

    [Range(typeof(decimal), "0.01", "9999999999")]
    public decimal Price { get; init; }
}

public sealed record ProductResponse(int Id, string Name, decimal Price);

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products").AddEndpointFilter<ValidationFilter>();

        group.MapGet("", async (AppDbContext db, CancellationToken ct) =>
        {
            var products = await db.Products.AsNoTracking()
                .OrderBy(product => product.Id)
                .Select(product => new ProductResponse(product.Id, product.Name, product.Price))
                .ToListAsync(ct);

            return Results.Ok(products);
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var product = await db.Products.AsNoTracking()
                .Where(product => product.Id == id)
                .Select(product => new ProductResponse(product.Id, product.Name, product.Price))
                .FirstOrDefaultAsync(ct);

            return product is null ? Results.NotFound() : Results.Ok(product);
        });

        group.MapPost("", async (CreateProductRequest request, AppDbContext db, CancellationToken ct) =>
        {
            var product = new Product { Name = request.Name.Trim(), Price = request.Price };
            db.Products.Add(product);
            await db.SaveChangesAsync(ct);

            var response = new ProductResponse(product.Id, product.Name, product.Price);
            return Results.Created($"/api/products/{product.Id}", response);
        });

        return app;
    }
}
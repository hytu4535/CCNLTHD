using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using StripeWebhooks.Api.DTOs;
using StripeWebhooks.Api.Models;
using StripeWebhooks.Tests.Utils;
using Xunit;

namespace StripeWebhooks.Tests.Integration;

public class ProductApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ProductApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProducts_ReturnsSeededProducts()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var products = await response.Content.ReadFromJsonAsync<List<Product>>();
        products.Should().NotBeNull();
        products.Should().NotBeEmpty();
        products?.Any(p => p.Name == "Stripe Developer T-Shirt").Should().BeTrue();
    }

    [Fact]
    public async Task GetProductById_ReturnsProduct_WhenExists()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products/1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var product = await response.Content.ReadFromJsonAsync<Product>();
        product.Should().NotBeNull();
        product!.Id.Should().Be(1);
        product.Name.Should().Be("Stripe Developer T-Shirt");
    }

    [Fact]
    public async Task GetProductById_ReturnsNotFound_WhenNotExists()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateProduct_ReturnsCreated_WhenValid()
    {
        var client = _factory.CreateClient();
        var dto = new CreateProductDto(null, "New Test Product Unique", 49.99m, 10);

        var response = await client.PostAsJsonAsync("/api/products", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var product = await response.Content.ReadFromJsonAsync<Product>();
        product.Should().NotBeNull();
        product!.Name.Should().Be("New Test Product Unique");
        product.Price.Should().Be(49.99m);
        product.StockQuantity.Should().Be(10);
    }

    [Fact]
    public async Task CreateProduct_ReturnsBadRequest_WhenInvalid()
    {
        var client = _factory.CreateClient();
        var dto = new CreateProductDto(null, "", -10m, -5); // Invalid name, price, stock

        var response = await client.PostAsJsonAsync("/api/products", dto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProduct_ReturnsBadRequest_WhenNameAlreadyExists()
    {
        var client = _factory.CreateClient();
        var dto = new CreateProductDto(null, "Stripe Developer T-Shirt", 19.99m, 10); // Seeded product name

        var response = await client.PostAsJsonAsync("/api/products", dto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProduct_ReturnsBadRequest_WhenIdAlreadyExists()
    {
        var client = _factory.CreateClient();
        var dto = new CreateProductDto(1, "Duplicate ID Product Unique", 19.99m, 10); // ID 1 already exists

        var response = await client.PostAsJsonAsync("/api/products", dto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProduct_ReturnsOk_WhenValid()
    {
        var client = _factory.CreateClient();

        // First create a product to update
        var createDto = new CreateProductDto(null, "Product To Update Unique", 19.99m, 5);
        var createRes = await client.PostAsJsonAsync("/api/products", createDto);
        var created = await createRes.Content.ReadFromJsonAsync<Product>();

        var updateDto = new UpdateProductDto("Updated Product Name Unique", 24.99m, 15);
        var response = await client.PutAsJsonAsync($"/api/products/{created!.Id}", updateDto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<Product>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated Product Name Unique");
        updated.Price.Should().Be(24.99m);
        updated.StockQuantity.Should().Be(15);
        updated.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateProduct_ReturnsBadRequest_WhenNameAlreadyExistsOnAnotherProduct()
    {
        var client = _factory.CreateClient();

        var createDto = new CreateProductDto(null, "Product For Duplicate Test", 19.99m, 5);
        var createRes = await client.PostAsJsonAsync("/api/products", createDto);
        var created = await createRes.Content.ReadFromJsonAsync<Product>();

        // Try to update with name of another seeded product ("Stripe Coffee Mug")
        var updateDto = new UpdateProductDto("Stripe Coffee Mug", 24.99m, 15);
        var response = await client.PutAsJsonAsync($"/api/products/{created!.Id}", updateDto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteProduct_ReturnsNoContent_WhenExists()
    {
        var client = _factory.CreateClient();

        // Create a product to delete
        var createDto = new CreateProductDto(null, "Product To Delete Unique", 9.99m, 2);
        var createRes = await client.PostAsJsonAsync("/api/products", createDto);
        var created = await createRes.Content.ReadFromJsonAsync<Product>();

        var response = await client.DeleteAsync($"/api/products/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getRes = await client.GetAsync($"/api/products/{created.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

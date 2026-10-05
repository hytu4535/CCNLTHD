using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using StripeWebhooks.Tests.Utils;
using Xunit;

namespace StripeWebhooks.Tests.Integration;

public sealed class ProductApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ProductApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ProductEndpoints_ValidateRequestsAndSupportCrud()
    {
        var invalidResponse = await _client.PostAsJsonAsync("/api/products", new
        {
            name = "",
            price = 0m,
            stockQuantity = -1
        });

        invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var createResponse = await _client.PostAsJsonAsync("/api/products", new
        {
            name = "  Keyboard  ",
            price = 49.99m,
            stockQuantity = 12
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        createResponse.Headers.Location.Should().NotBeNull();
        var createdProduct = await createResponse.Content.ReadFromJsonAsync<ProductPayload>();
        createdProduct.Should().NotBeNull();
        createdProduct!.Name.Should().Be("Keyboard");
        createdProduct.StockQuantity.Should().Be(12);
        createdProduct.CreatedAt.Should().NotBe(default);
        createdProduct.UpdatedAt.Should().BeNull();

        var duplicateResponse = await _client.PostAsJsonAsync("/api/products", new
        {
            name = "keyboard",
            price = 55m,
            stockQuantity = 1
        });
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var updateResponse = await _client.PutAsJsonAsync($"/api/products/{createdProduct.Id}", new
        {
            name = "Mechanical Keyboard",
            price = 59.99m,
            stockQuantity = 8
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedProduct = await updateResponse.Content.ReadFromJsonAsync<ProductPayload>();
        updatedProduct!.Name.Should().Be("Mechanical Keyboard");
        updatedProduct.StockQuantity.Should().Be(8);
        updatedProduct.UpdatedAt.Should().NotBeNull();

        var deleteResponse = await _client.DeleteAsync($"/api/products/{createdProduct.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync($"/api/products/{createdProduct.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record ProductPayload(
        int Id,
        string Name,
        decimal Price,
        int StockQuantity,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);
}

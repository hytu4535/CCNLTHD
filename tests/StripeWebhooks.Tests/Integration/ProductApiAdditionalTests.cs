
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using StripeWebhooks.Api.DTOs;
using StripeWebhooks.Api.Models;
using StripeWebhooks.Tests.Utils;
using Xunit;

namespace StripeWebhooks.Tests.Integration;

public class ProductApiAdditionalTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ProductApiAdditionalTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProducts_FiltersBySearch_CaseInsensitive()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products?search=developer");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var products = await response.Content.ReadFromJsonAsync<List<Product>>();

        products.Should().NotBeNull();
        products!.Should().ContainSingle();
        products?[0].Name.Should().Be("Stripe Developer T-Shirt");
    }

    [Fact]
    public async Task GetProducts_ReturnsEmpty_WhenSearchDoesNotMatch()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products?search=does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var products = await response.Content.ReadFromJsonAsync<List<Product>>();

        products.Should().NotBeNull();
        products!.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateProduct_TrimsNameBeforeSaving()
    {
        var client = _factory.CreateClient();
        var dto = new CreateProductDto(null, "   Trimmed Product   ", 12.50m, 3);

        var response = await client.PostAsJsonAsync("/api/products", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await response.Content.ReadFromJsonAsync<Product>();
        product!.Name.Should().Be("Trimmed Product");
    }

    [Fact]
    public async Task CreateProduct_ReturnsBadRequest_WhenNameExceeds100Characters()
    {
        var client = _factory.CreateClient();
        var dto = new CreateProductDto(null, new string('A', 101), 10m, 1);

        var response = await client.PostAsJsonAsync("/api/products", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProduct_ReturnsNotFound_WhenIdDoesNotExist()
    {
        var client = _factory.CreateClient();
        var dto = new UpdateProductDto("Valid Product", 10m, 1);

        var response = await client.PutAsJsonAsync("/api/products/999999", dto);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateProduct_ReturnsBadRequest_WhenDataIsInvalid()
    {
        var client = _factory.CreateClient();
        var dto = new UpdateProductDto("", -1m, -10);

        var response = await client.PutAsJsonAsync("/api/products/1", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteProduct_ReturnsNotFound_WhenIdDoesNotExist()
    {
        var client = _factory.CreateClient();

        var response = await client.DeleteAsync("/api/products/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

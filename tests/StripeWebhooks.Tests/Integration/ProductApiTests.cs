using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
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

    // ============================================================
    // GET /api/products
    // ============================================================

    [Fact]
    public async Task GetProducts_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var products =
            await response.Content.ReadFromJsonAsync<List<Product>>();

        products.Should().NotBeNull();
        products.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetProducts_ReturnsSeededProduct()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var products =
            await response.Content.ReadFromJsonAsync<List<Product>>();

        products.Should().NotBeNull();

        products!
            .Any(p => p.Name == "Stripe Developer T-Shirt")
            .Should().BeTrue();
    }

    // ============================================================
    // GET /api/products/{id}
    // ============================================================

    [Fact]
    public async Task GetProductById_ReturnsOk_WhenProductExists()
    {
        var client = _factory.CreateClient();

        var response =
            await client.GetAsync("/api/products/1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var product =
            await response.Content.ReadFromJsonAsync<Product>();

        product.Should().NotBeNull();
        product!.Id.Should().Be(1);
        product.Name.Should().Be("Stripe Developer T-Shirt");
    }

    [Fact]
    public async Task GetProductById_ReturnsNotFound_WhenProductDoesNotExist()
    {
        var client = _factory.CreateClient();

        var response =
            await client.GetAsync("/api/products/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetProductById_ReturnsNotFound_WhenIdIsNegative()
    {
        var client = _factory.CreateClient();

        var response =
            await client.GetAsync("/api/products/-1");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ============================================================
    // POST /api/products
    //
    // JSON:
    // {
    //   "id": 0,
    //   "name": "string",
    //   "price": 0.01,
    //   "stockQuantity": 2147483647
    // }
    // ============================================================

    [Fact]
    public async Task CreateProduct_ReturnsCreated_WhenValid()
    {
        var client = _factory.CreateClient();

        var request = new
        {
            id = 0,
            name = "New Test Product Unique",
            price = 49.99m,
            stockQuantity = 10
        };

        var response =
            await client.PostAsJsonAsync(
                "/api/products",
                request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var product =
            await response.Content.ReadFromJsonAsync<Product>();

        product.Should().NotBeNull();
        product!.Name.Should().Be("New Test Product Unique");
        product.Price.Should().Be(49.99m);
        product.StockQuantity.Should().Be(10);
        product.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CreateProduct_ReturnsBadRequest_WhenNameIsEmpty()
    {
        var client = _factory.CreateClient();

        var request = new
        {
            id = 0,
            name = "",
            price = 10m,
            stockQuantity = 10
        };

        var response =
            await client.PostAsJsonAsync(
                "/api/products",
                request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProduct_ReturnsBadRequest_WhenNameIsWhitespace()
    {
        var client = _factory.CreateClient();

        var request = new
        {
            id = 0,
            name = "     ",
            price = 10m,
            stockQuantity = 10
        };

        var response =
            await client.PostAsJsonAsync(
                "/api/products",
                request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProduct_ReturnsBadRequest_WhenPriceIsNegative()
    {
        var client = _factory.CreateClient();

        var request = new
        {
            id = 0,
            name = "Invalid Price Product",
            price = -1m,
            stockQuantity = 10
        };

        var response =
            await client.PostAsJsonAsync(
                "/api/products",
                request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProduct_ReturnsBadRequest_WhenStockIsNegative()
    {
        var client = _factory.CreateClient();

        var request = new
        {
            id = 0,
            name = "Invalid Stock Product",
            price = 10m,
            stockQuantity = -1
        };

        var response =
            await client.PostAsJsonAsync(
                "/api/products",
                request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProduct_ReturnsBadRequest_WhenNameAlreadyExists()
    {
        var client = _factory.CreateClient();

        var request = new
        {
            id = 0,
            name = "Stripe Developer T-Shirt",
            price = 19.99m,
            stockQuantity = 10
        };

        var response =
            await client.PostAsJsonAsync(
                "/api/products",
                request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProduct_TrimsNameBeforeSaving()
    {
        var client = _factory.CreateClient();

        var request = new
        {
            id = 0,
            name = "   Trimmed Product Unique   ",
            price = 12.50m,
            stockQuantity = 3
        };

        var response =
            await client.PostAsJsonAsync(
                "/api/products",
                request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var product =
            await response.Content.ReadFromJsonAsync<Product>();

        product.Should().NotBeNull();
        product!.Name.Should().Be("Trimmed Product Unique");
    }

    [Fact]
    public async Task CreateProduct_ReturnsBadRequest_WhenNameExceeds100Characters()
    {
        var client = _factory.CreateClient();

        var request = new
        {
            id = 0,
            name = new string('A', 101),
            price = 10m,
            stockQuantity = 1
        };

        var response =
            await client.PostAsJsonAsync(
                "/api/products",
                request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProduct_ReturnsCreated_WhenPriceIsZero()
    {
        var client = _factory.CreateClient();

        var request = new
        {
            id = 0,
            name = "Zero Price Product Unique",
            price = 0m,
            stockQuantity = 1
        };

        var response =
            await client.PostAsJsonAsync(
                "/api/products",
                request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProduct_ReturnsCreated_WhenStockIsZero()
    {
        var client = _factory.CreateClient();

        var request = new
        {
            id = 0,
            name = "Zero Stock Product Unique",
            price = 10m,
            stockQuantity = 0
        };

        var response =
            await client.PostAsJsonAsync(
                "/api/products",
                request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var product =
            await response.Content.ReadFromJsonAsync<Product>();

        product.Should().NotBeNull();
        product!.StockQuantity.Should().Be(0);
    }

    // ============================================================
    // PUT /api/products/{id}
    // ============================================================

    [Fact]
    public async Task UpdateProduct_ReturnsOk_WhenValid()
    {
        var client = _factory.CreateClient();

        // Create product first
        var createRequest = new
        {
            id = 0,
            name = "Product To Update Unique",
            price = 19.99m,
            stockQuantity = 5
        };

        var createResponse =
            await client.PostAsJsonAsync(
                "/api/products",
                createRequest);

        createResponse.StatusCode
            .Should().Be(HttpStatusCode.Created);

        var created =
            await createResponse.Content.ReadFromJsonAsync<Product>();

        created.Should().NotBeNull();

        // Update product
        var updateRequest = new
        {
            name = "Updated Product Unique",
            price = 24.99m,
            stockQuantity = 15
        };

        var response =
            await client.PutAsJsonAsync(
                $"/api/products/{created!.Id}",
                updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated =
            await response.Content.ReadFromJsonAsync<Product>();

        updated.Should().NotBeNull();
        updated!.Id.Should().Be(created.Id);
        updated.Name.Should().Be("Updated Product Unique");
        updated.Price.Should().Be(24.99m);
        updated.StockQuantity.Should().Be(15);
    }

    [Fact]
    public async Task UpdateProduct_ReturnsNotFound_WhenProductDoesNotExist()
    {
        var client = _factory.CreateClient();

        var request = new
        {
            name = "Updated Product",
            price = 20m,
            stockQuantity = 10
        };

        var response =
            await client.PutAsJsonAsync(
                "/api/products/999999",
                request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateProduct_ReturnsBadRequest_WhenNameIsEmpty()
    {
        var client = _factory.CreateClient();

        var request = new
        {
            name = "",
            price = 20m,
            stockQuantity = 10
        };

        var response =
            await client.PutAsJsonAsync(
                "/api/products/1",
                request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProduct_ReturnsBadRequest_WhenPriceIsNegative()
    {
        var client = _factory.CreateClient();

        var request = new
        {
            name = "Invalid Update Price",
            price = -10m,
            stockQuantity = 10
        };

        var response =
            await client.PutAsJsonAsync(
                "/api/products/1",
                request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProduct_ReturnsBadRequest_WhenStockIsNegative()
    {
        var client = _factory.CreateClient();

        var request = new
        {
            name = "Invalid Update Stock",
            price = 10m,
            stockQuantity = -1
        };

        var response =
            await client.PutAsJsonAsync(
                "/api/products/1",
                request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProduct_ReturnsBadRequest_WhenNameAlreadyExists()
    {
        var client = _factory.CreateClient();

        var createRequest = new
        {
            id = 0,
            name = "Product For Duplicate Update Unique",
            price = 19.99m,
            stockQuantity = 5
        };

        var createResponse =
            await client.PostAsJsonAsync(
                "/api/products",
                createRequest);

        createResponse.StatusCode
            .Should().Be(HttpStatusCode.Created);

        var created =
            await createResponse.Content.ReadFromJsonAsync<Product>();

        created.Should().NotBeNull();

        var updateRequest = new
        {
            name = "Stripe Coffee Mug",
            price = 24.99m,
            stockQuantity = 15
        };

        var response =
            await client.PutAsJsonAsync(
                $"/api/products/{created!.Id}",
                updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProduct_TrimsNameBeforeSaving()
    {
        var client = _factory.CreateClient();

        var createRequest = new
        {
            id = 0,
            name = "Product Before Update Unique",
            price = 10m,
            stockQuantity = 5
        };

        var createResponse =
            await client.PostAsJsonAsync(
                "/api/products",
                createRequest);

        var created =
            await createResponse.Content.ReadFromJsonAsync<Product>();

        created.Should().NotBeNull();

        var updateRequest = new
        {
            name = "   Updated Trimmed Product Unique   ",
            price = 15m,
            stockQuantity = 8
        };

        var response =
            await client.PutAsJsonAsync(
                $"/api/products/{created!.Id}",
                updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated =
            await response.Content.ReadFromJsonAsync<Product>();

        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated Trimmed Product Unique");
    }

    // ============================================================
    // DELETE /api/products/{id}
    // ============================================================

    [Fact]
    public async Task DeleteProduct_ReturnsNoContent_WhenProductExists()
    {
        var client = _factory.CreateClient();

        var createRequest = new
        {
            id = 0,
            name = "Product To Delete Unique",
            price = 9.99m,
            stockQuantity = 2
        };

        var createResponse =
            await client.PostAsJsonAsync(
                "/api/products",
                createRequest);

        createResponse.StatusCode
            .Should().Be(HttpStatusCode.Created);

        var created =
            await createResponse.Content.ReadFromJsonAsync<Product>();

        created.Should().NotBeNull();

        var deleteResponse =
            await client.DeleteAsync(
                $"/api/products/{created!.Id}");

        deleteResponse.StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        // Verify that the product no longer exists.
        var getResponse =
            await client.GetAsync(
                $"/api/products/{created.Id}");

        getResponse.StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProduct_ReturnsNotFound_WhenProductDoesNotExist()
    {
        var client = _factory.CreateClient();

        var response =
            await client.DeleteAsync(
                "/api/products/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProduct_ReturnsNotFound_WhenIdIsNegative()
    {
        var client = _factory.CreateClient();

        var response =
            await client.DeleteAsync(
                "/api/products/-1");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ============================================================
    // FULL CRUD
    // ============================================================

    [Fact]
    public async Task Product_FullCrudFlow_WorksCorrectly()
    {
        var client = _factory.CreateClient();

        // --------------------------------------------------------
        // 1. CREATE
        // --------------------------------------------------------

        var createRequest = new
        {
            id = 0,
            name = "Full CRUD Product Unique",
            price = 100m,
            stockQuantity = 20
        };

        var createResponse =
            await client.PostAsJsonAsync(
                "/api/products",
                createRequest);

        createResponse.StatusCode
            .Should().Be(HttpStatusCode.Created);

        var created =
            await createResponse.Content.ReadFromJsonAsync<Product>();

        created.Should().NotBeNull();
        created!.Id.Should().BeGreaterThan(0);
        created.Name.Should().Be("Full CRUD Product Unique");
        created.Price.Should().Be(100m);
        created.StockQuantity.Should().Be(20);

        // --------------------------------------------------------
        // 2. GET
        // --------------------------------------------------------

        var getResponse =
            await client.GetAsync(
                $"/api/products/{created.Id}");

        getResponse.StatusCode
            .Should().Be(HttpStatusCode.OK);

        var retrieved =
            await getResponse.Content.ReadFromJsonAsync<Product>();

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
        retrieved.Name.Should().Be("Full CRUD Product Unique");

        // --------------------------------------------------------
        // 3. UPDATE
        // --------------------------------------------------------

        var updateRequest = new
        {
            name = "Full CRUD Updated Product Unique",
            price = 150m,
            stockQuantity = 30
        };

        var updateResponse =
            await client.PutAsJsonAsync(
                $"/api/products/{created.Id}",
                updateRequest);

        updateResponse.StatusCode
            .Should().Be(HttpStatusCode.OK);

        // --------------------------------------------------------
        // 4. VERIFY UPDATE
        // --------------------------------------------------------

        var getUpdatedResponse =
            await client.GetAsync(
                $"/api/products/{created.Id}");

        getUpdatedResponse.StatusCode
            .Should().Be(HttpStatusCode.OK);

        var updated =
            await getUpdatedResponse.Content
                .ReadFromJsonAsync<Product>();

        updated.Should().NotBeNull();
        updated!.Id.Should().Be(created.Id);
        updated.Name
            .Should().Be("Full CRUD Updated Product Unique");
        updated.Price.Should().Be(150m);
        updated.StockQuantity.Should().Be(30);

        // --------------------------------------------------------
        // 5. DELETE
        // --------------------------------------------------------

        var deleteResponse =
            await client.DeleteAsync(
                $"/api/products/{created.Id}");

        deleteResponse.StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        // --------------------------------------------------------
        // 6. VERIFY DELETE
        // --------------------------------------------------------

        var getDeletedResponse =
            await client.GetAsync(
                $"/api/products/{created.Id}");

        getDeletedResponse.StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }
}
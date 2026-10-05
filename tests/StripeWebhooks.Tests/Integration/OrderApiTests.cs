using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using StripeWebhooks.Tests.Utils;
using Xunit;

namespace StripeWebhooks.Tests.Integration;

public class OrderApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public OrderApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ============================================================
    // CREATE ORDER
    // ============================================================

    [Fact]
    public async Task CreateOrder_WithValidData_ReturnsCreated()
    {
        // Arrange
        var product = await CreateProductAsync("Coffee", 4.5m);

        var request = new
        {
            productId = product.Id,
            quantity = 2,
            paymentIntentId = "pi_create_order"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/orders",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var order =
            await response.Content.ReadFromJsonAsync<OrderPayload>();

        order.Should().NotBeNull();
        order!.ProductId.Should().Be(product.Id);
        order.ProductName.Should().Be("Coffee");
        order.UnitPrice.Should().Be(4.5m);
        order.Quantity.Should().Be(2);
        order.Total.Should().Be(9m);
        order.PaymentIntentId.Should().Be("pi_create_order");
        order.PaymentStatus.Should().Be("Pending");
    }

    [Fact]
    public async Task CreateOrder_WithNonExistingProduct_ReturnsNotFound()
    {
        // Arrange
        var request = new
        {
            productId = 999999,
            quantity = 1,
            paymentIntentId = "pi_missing_product"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/orders",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("Product was not found");
    }

    [Fact]
    public async Task CreateOrder_WithDuplicatePaymentIntent_ReturnsConflict()
    {
        // Arrange
        var product = await CreateProductAsync("Laptop", 1000m);

        var request = new
        {
            productId = product.Id,
            quantity = 1,
            paymentIntentId = "pi_duplicate"
        };

        // Act
        var firstResponse = await _client.PostAsJsonAsync(
            "/api/orders",
            request);

        var secondResponse = await _client.PostAsJsonAsync(
            "/api/orders",
            request);

        // Assert
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await secondResponse.Content.ReadAsStringAsync();

        body.Should().Contain("already linked");
    }

    // ============================================================
    // VALIDATION
    // ============================================================

    [Fact]
    public async Task CreateOrder_WithQuantityZero_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            productId = 1,
            quantity = 0,
            paymentIntentId = "pi_invalid_quantity"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/orders",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("Quantity");
    }

    [Fact]
    public async Task CreateOrder_WithQuantityGreaterThanMaximum_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            productId = 1,
            quantity = 10001,
            paymentIntentId = "pi_quantity_too_large"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/orders",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("Quantity");
    }

    [Fact]
    public async Task CreateOrder_WithInvalidPaymentIntentId_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            productId = 1,
            quantity = 1,
            paymentIntentId = "not-a-payment-intent"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/orders",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("PaymentIntentId");
    }

    [Fact]
    public async Task CreateOrder_WithEmptyPaymentIntentId_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            productId = 1,
            quantity = 1,
            paymentIntentId = ""
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/orders",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("PaymentIntentId");
    }

    [Fact]
    public async Task CreateOrder_WithInvalidProductId_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            productId = 0,
            quantity = 1,
            paymentIntentId = "pi_invalid_product_id"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/orders",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("ProductId");
    }

    // ============================================================
    // GET ORDER
    // ============================================================

    [Fact]
    public async Task GetOrder_WithExistingId_ReturnsOrder()
    {
        // Arrange
        var product = await CreateProductAsync("Mouse", 25m);

        var order = await CreateOrderAsync(
            product.Id,
            3,
            "pi_get_order");

        // Act
        var response = await _client.GetAsync(
            $"/api/orders/{order.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result =
            await response.Content.ReadFromJsonAsync<OrderPayload>();

        result.Should().NotBeNull();
        result!.Id.Should().Be(order.Id);
        result.ProductName.Should().Be("Mouse");
        result.Quantity.Should().Be(3);
        result.Total.Should().Be(75m);
    }

    [Fact]
    public async Task GetOrder_WithNonExistingId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/orders/999999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ============================================================
    // GET ALL ORDERS
    // ============================================================

    [Fact]
    public async Task GetOrders_ReturnsOrders()
    {
        // Arrange
        var product = await CreateProductAsync(
            "Keyboard",
            50m);

        await CreateOrderAsync(
            product.Id,
            1,
            "pi_get_all_1");

        await CreateOrderAsync(
            product.Id,
            2,
            "pi_get_all_2");

        // Act
        var response = await _client.GetAsync(
            "/api/orders");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var orders =
            await response.Content.ReadFromJsonAsync<List<OrderPayload>>();

        orders.Should().NotBeNull();
        orders!.Should().Contain(order =>
            order.PaymentIntentId == "pi_get_all_1");

        orders.Should().Contain(order =>
            order.PaymentIntentId == "pi_get_all_2");
    }

    // ============================================================
    // UPDATE ORDER
    // ============================================================

    [Fact]
    public async Task UpdateOrder_WithValidData_ReturnsUpdatedOrder()
    {
        // Arrange
        var product1 = await CreateProductAsync(
            "Old Product",
            20m);

        var product2 = await CreateProductAsync(
            "New Product",
            30m);

        var order = await CreateOrderAsync(
            product1.Id,
            2,
            "pi_update_order");

        var request = new
        {
            productId = product2.Id,
            quantity = 3
        };

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/orders/{order.Id}",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result =
            await response.Content.ReadFromJsonAsync<OrderPayload>();

        result.Should().NotBeNull();
        result!.ProductId.Should().Be(product2.Id);
        result.ProductName.Should().Be("New Product");
        result.UnitPrice.Should().Be(30m);
        result.Quantity.Should().Be(3);
        result.Total.Should().Be(90m);
    }

    [Fact]
    public async Task UpdateOrder_WithNonExistingOrder_ReturnsNotFound()
    {
        // Arrange
        var product = await CreateProductAsync(
            $"Product-{Guid.NewGuid():N}",
            20m);

        var request = new
        {
            productId = product.Id,
            quantity = 2
        };

        // Act
        var response = await _client.PutAsJsonAsync(
            "/api/orders/999999",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateOrder_WithNonExistingProduct_ReturnsNotFound()
    {
        // Arrange
        var product = await CreateProductAsync(
            $"Product-{Guid.NewGuid():N}",
            20m);

        var order = await CreateOrderAsync(
            product.Id,
            1,
            $"pi_update_missing_product_{Guid.NewGuid():N}");

        var request = new
        {
            productId = 999999,
            quantity = 2
        };

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/orders/{order.Id}",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Product was not found");
    }

    [Fact]
    public async Task UpdateOrder_WithInvalidQuantity_ReturnsBadRequest()
    {
        // Arrange
        var product = await CreateProductAsync(
            "Product",
            20m);

        var order = await CreateOrderAsync(
            product.Id,
            1,
            "pi_update_invalid_quantity");

        var request = new
        {
            productId = product.Id,
            quantity = 0
        };

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/orders/{order.Id}",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("Quantity");
    }

    // ============================================================
    // DELETE ORDER
    // ============================================================

    [Fact]
    public async Task DeleteOrder_WithExistingId_ReturnsNoContent()
    {
        // Arrange
        var product = await CreateProductAsync(
            "Delete Product",
            15m);

        var order = await CreateOrderAsync(
            product.Id,
            1,
            "pi_delete_order");

        // Act
        var response = await _client.DeleteAsync(
            $"/api/orders/{order.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync(
            $"/api/orders/{order.Id}");

        getResponse.StatusCode.Should().Be(
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteOrder_WithNonExistingId_ReturnsNotFound()
    {
        // Act
        var response = await _client.DeleteAsync(
            "/api/orders/999999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ============================================================
    // SWAGGER
    // ============================================================

    [Fact]
    public async Task Swagger_IsAvailable()
    {
        // Act
        var response = await _client.GetAsync(
            "/swagger/v1/swagger.json");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("/api/orders");
    }

    // ============================================================
    // HELPER METHODS
    // ============================================================

    private async Task<ProductPayload> CreateProductAsync(
        string name,
        decimal price)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/products",
            new
            {
                name,
                price
            });

        response.StatusCode.Should().Be(
            HttpStatusCode.Created);

        var product =
            await response.Content.ReadFromJsonAsync<ProductPayload>();

        product.Should().NotBeNull();

        return product!;
    }

    private async Task<OrderPayload> CreateOrderAsync(
        int productId,
        int quantity,
        string paymentIntentId)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/orders",
            new
            {
                productId,
                quantity,
                paymentIntentId
            });

        response.StatusCode.Should().Be(
            HttpStatusCode.Created);

        var order =
            await response.Content.ReadFromJsonAsync<OrderPayload>();

        order.Should().NotBeNull();

        return order!;
    }

    // ============================================================
    // RESPONSE MODELS
    // ============================================================

    private sealed record ProductPayload(
        int Id,
        string Name,
        decimal Price);

    private sealed record OrderPayload(
        int Id,
        int ProductId,
        string ProductName,
        decimal UnitPrice,
        int Quantity,
        decimal Total,
        string? PaymentIntentId,
        string PaymentStatus);
}
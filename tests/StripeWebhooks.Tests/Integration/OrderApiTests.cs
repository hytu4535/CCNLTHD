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

    [Fact]
    public async Task Orders_CanBeCreatedReadUpdatedAndDeletedWithAProduct()
    {
        var createProduct = await _client.PostAsJsonAsync("/api/products", new { name = "Coffee", price = 4.5m });
        createProduct.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await createProduct.Content.ReadFromJsonAsync<ProductPayload>();

        const string paymentIntentId = "pi_order_crud";
        var createOrder = await _client.PostAsJsonAsync("/api/orders", new
        {
            productId = product!.Id,
            quantity = 2,
            paymentIntentId
        });
        createOrder.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await createOrder.Content.ReadFromJsonAsync<OrderPayload>();
        order!.ProductName.Should().Be("Coffee");
        order.Total.Should().Be(9m);
        order.PaymentIntentId.Should().Be(paymentIntentId);
        order.PaymentStatus.Should().Be("Pending");

        var readOrder = await _client.GetAsync($"/api/orders/{order.Id}");
        readOrder.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateOrder = await _client.PutAsJsonAsync($"/api/orders/{order.Id}", new { productId = product.Id, quantity = 3 });
        updateOrder.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updateOrder.Content.ReadFromJsonAsync<OrderPayload>())!.Total.Should().Be(13.5m);

        var deleteOrder = await _client.DeleteAsync($"/api/orders/{order.Id}");
        deleteOrder.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync($"/api/orders/{order.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateOrder_ReturnsValidationErrorsForInvalidQuantity()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new
        {
            productId = 1,
            quantity = 0,
            paymentIntentId = "pi_invalid_quantity"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Quantity");
    }

    [Fact]
    public async Task CreateOrder_RequiresStripePaymentIntentId()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new
        {
            productId = 1,
            quantity = 1,
            paymentIntentId = "not-a-payment-intent"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PaymentIntentId");
    }

    [Fact]
    public async Task Swagger_IsAvailable()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("/api/orders");
    }

    private sealed record ProductPayload(int Id, string Name, decimal Price);
    private sealed record OrderPayload(
        int Id,
        string ProductName,
        decimal Total,
        string? PaymentIntentId,
        string PaymentStatus);
}
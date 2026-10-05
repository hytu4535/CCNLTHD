using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StripeWebhooks.Api.Persistence;
using StripeWebhooks.Api.Persistence.Entities;
using StripeWebhooks.Tests.Utils;
using Xunit;

namespace StripeWebhooks.Tests.Integration;


public class StripeWebhookApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public StripeWebhookApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Webhook_Returns200_AndPersists_AndIsIdempotent()
    {
        var client = _factory.CreateClient();

        var payload = """
{
  "id": "evt_123",
  "object": "event",
  "api_version": "2024-09-30.acacia",
  "created": 1710000000,
  "data": {
    "object": {
      "id": "pi_123",
      "object": "payment_intent",
      "amount": 1000,
      "currency": "usd",
      "status": "succeeded"
    },
    "previous_attributes": {}
  },
  "livemode": false,
  "pending_webhooks": 1,
  "request": { "id": "req_123", "idempotency_key": "idem_123" },
  "type": "payment_intent.succeeded"
}
""";

        var sigHeader = StripeTestSignatures.CreateStripeSignatureHeader(
            payload,
            TestWebApplicationFactory.WebhookSecret
        );

        // 1st delivery
        var res1 = await PostWebhookAsync(client, payload, sigHeader);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2nd delivery (duplicate)
        var res2 = await PostWebhookAsync(client, payload, sigHeader);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert DB state
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // NOTE: adjust DbSet/property names if yours differ
        db.ProcessedEvents.Count(x => x.EventId == "evt_123").Should().Be(1);
        db.PaymentEvents.Count(x => x.StripeEventId == "evt_123").Should().Be(1);
    }

    [Fact]
    public async Task Webhook_Returns400_WhenSignatureMissing()
    {
        var client = _factory.CreateClient();

        var payload = """
{
  "id": "evt_456",
  "object": "event",
  "api_version": "2024-09-30.acacia",
  "created": 1710000000,
  "data": { "object": { "id": "pi_456", "object": "payment_intent" }, "previous_attributes": {} },
  "livemode": false,
  "pending_webhooks": 1,
  "request": { "id": "req_456", "idempotency_key": "idem_456" },
  "type": "payment_intent.succeeded"
}
""";

        var req = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("evt_order_payment_succeeded", "pi_order_succeeded", "payment_intent.succeeded", OrderPaymentStatus.Paid)]
    [InlineData("evt_order_payment_failed", "pi_order_failed", "payment_intent.payment_failed", OrderPaymentStatus.Failed)]
    public async Task Webhook_UpdatesLinkedOrderPaymentStatus(
        string eventId,
        string paymentIntentId,
        string eventType,
        OrderPaymentStatus expectedStatus)
    {
        var client = _factory.CreateClient();
        int orderId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var product = new Product { Name = "Webhook test product", Price = 12m };
            db.Products.Add(product);
            await db.SaveChangesAsync();

            var order = new Order
            {
                ProductId = product.Id,
                Product = product,
                PaymentIntentId = paymentIntentId,
                Quantity = 1
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        var payload = $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-09-30.acacia",
  "created": 1710000000,
  "data": {
    "object": {
      "id": "{{paymentIntentId}}",
      "object": "payment_intent",
      "amount": 1200,
      "amount_received": 1200,
      "currency": "nzd",
      "status": "{{(expectedStatus == OrderPaymentStatus.Paid ? "succeeded" : "requires_payment_method")}}"
    },
    "previous_attributes": {}
  },
  "livemode": false,
  "pending_webhooks": 1,
  "request": { "id": "req_order", "idempotency_key": "idem_order" },
  "type": "{{eventType}}"
}
""";
        var signature = StripeTestSignatures.CreateStripeSignatureHeader(
            payload,
            TestWebApplicationFactory.WebhookSecret);
        var response = await PostWebhookAsync(client, payload, signature);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var assertScope = _factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orderAfterWebhook = await assertDb.Orders.FindAsync(orderId);
        orderAfterWebhook!.PaymentStatus.Should().Be(expectedStatus);
    }

    private static Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, string payload, string signatureHeader)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        req.Headers.TryAddWithoutValidation("Stripe-Signature", signatureHeader);
        req.Headers.TryAddWithoutValidation("X-Correlation-Id", Guid.NewGuid().ToString("N"));

        return client.SendAsync(req);
    }
}

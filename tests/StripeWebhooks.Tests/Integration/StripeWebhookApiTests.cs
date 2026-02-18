using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StripeWebhooks.Api.Persistence;
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

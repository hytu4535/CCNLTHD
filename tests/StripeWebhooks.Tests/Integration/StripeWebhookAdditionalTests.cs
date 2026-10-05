
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StripeWebhooks.Api.Persistence;
using StripeWebhooks.Tests.Utils;
using Xunit;

namespace StripeWebhooks.Tests.Integration;

/// <summary>
/// Kiểm thử bổ sung cho Stripe Webhook: chữ ký, payload, các event và idempotency.
/// </summary>
public class StripeWebhookAdditionalTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public StripeWebhookAdditionalTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Kiểm tra chữ ký Stripe sai bị từ chối và event không được ghi nhận.</summary>
    [Fact]
    public async Task Webhook_Returns400_WhenSignatureIsInvalid()
    {
        var client = _factory.CreateClient();
        // Payload hợp lệ nhưng dùng chữ ký giả để kiểm tra xác thực HMAC.
        var payload = PaymentIntentPayload("evt_invalid_signature", "pi_invalid");

        var response = await PostWebhookAsync(
            client,
            payload,
            "t=1710000000,v1=deadbeef");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Event bị từ chối không được đánh dấu là đã xử lý.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ProcessedEvents.Any(x => x.EventId == "evt_invalid_signature").Should().BeFalse();
    }

    /// <summary>Kiểm tra payload JSON lỗi cú pháp bị từ chối.</summary>
    [Fact]
    public async Task Webhook_Returns400_WhenPayloadIsMalformed()
    {
        var client = _factory.CreateClient();
        // JSON không hợp lệ phải bị webhook từ chối.
        const string payload = "{ this is not valid json";

        var signature = StripeTestSignatures.CreateStripeSignatureHeader(
            payload,
            TestWebApplicationFactory.WebhookSecret);

        var response = await PostWebhookAsync(client, payload, signature);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Kiểm tra event payment_intent.payment_failed được lưu thành PaymentEvent.</summary>
    [Fact]
    public async Task Webhook_PaymentFailed_PersistsPaymentEvent()
    {
        var client = _factory.CreateClient();
        // Dùng event ID riêng để tránh trùng với các test khác.
        const string eventId = "evt_payment_failed_test";

        var payload = PaymentIntentFailedPayload(eventId, "pi_failed_test");

        var signature = StripeTestSignatures.CreateStripeSignatureHeader(
            payload,
            TestWebApplicationFactory.WebhookSecret);

        var response = await PostWebhookAsync(client, payload, signature);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Event phải được persist vào PaymentEvents với đầy đủ thông tin quan trọng.
        var paymentEvent = db.PaymentEvents.Single(x => x.StripeEventId == eventId);
        paymentEvent.Kind.Should().Be("payment_intent.payment_failed");
        paymentEvent.PaymentIntentId.Should().Be("pi_failed_test");
        paymentEvent.Amount.Should().Be(1500);
        paymentEvent.Currency.Should().Be("usd");
    }

    /// <summary>Kiểm tra event refund.created được lưu và liên kết đúng PaymentIntent.</summary>
    [Fact]
    public async Task Webhook_RefundCreated_PersistsRefundEvent()
    {
        var client = _factory.CreateClient();
        // Kiểm tra riêng event refund.created.
        const string eventId = "evt_refund_created_test";

        var payload = RefundCreatedPayload(eventId, "re_test_refund", "pi_refund_test");

        var signature = StripeTestSignatures.CreateStripeSignatureHeader(
            payload,
            TestWebApplicationFactory.WebhookSecret);

        var response = await PostWebhookAsync(client, payload, signature);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Refund phải được lưu với PaymentIntentId, amount và currency tương ứng.
        var paymentEvent = db.PaymentEvents.Single(x => x.StripeEventId == eventId);
        paymentEvent.Kind.Should().Be("refund.created");
        paymentEvent.PaymentIntentId.Should().Be("pi_refund_test");
        paymentEvent.Amount.Should().Be(500);
        paymentEvent.Currency.Should().Be("usd");
    }

    /// <summary>Kiểm tra event không được hỗ trợ vẫn trả 200 nhưng không tạo PaymentEvent.</summary>
    [Fact]
    public async Task Webhook_UnsupportedEvent_Returns200_AndDoesNotCreatePaymentEvent()
    {
        var client = _factory.CreateClient();
        // customer.created không nằm trong nhóm event mà ứng dụng xử lý.
        const string eventId = "evt_unsupported_test";

        var payload = """
{
  "id": "evt_unsupported_test",
  "object": "event",
  "api_version": "2024-09-30.acacia",
  "created": 1710000000,
  "data": {
    "object": {
      "id": "cus_test",
      "object": "customer"
    },
    "previous_attributes": {}
  },
  "livemode": false,
  "pending_webhooks": 1,
  "request": { "id": "req_test" },
  "type": "customer.created"
}
""";

        var signature = StripeTestSignatures.CreateStripeSignatureHeader(
            payload,
            TestWebApplicationFactory.WebhookSecret);

        var response = await PostWebhookAsync(client, payload, signature);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Event vẫn được đánh dấu đã xử lý để hỗ trợ idempotency.
        db.ProcessedEvents.Any(x => x.EventId == eventId).Should().BeTrue();
        // Nhưng event không được chuyển thành PaymentEvent vì loại event không được hỗ trợ.
        db.PaymentEvents.Any(x => x.StripeEventId == eventId).Should().BeFalse();
    }

    /// <summary>Hàm tiện ích tạo HTTP POST tới webhook kèm Stripe-Signature.</summary>
    private static async Task<HttpResponseMessage> PostWebhookAsync(
        HttpClient client,
        string payload,
        string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("Stripe-Signature", signature);

        return await client.SendAsync(request);
    }

    /// <summary>Tạo payload payment_intent.succeeded dùng trong test chữ ký.</summary>
    private static string PaymentIntentPayload(string eventId, string paymentIntentId) =>
        $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-09-30.acacia",
  "created": 1710000000,
  "data": {
    "object": {
      "id": "{{paymentIntentId}}",
      "object": "payment_intent",
      "amount": 1000,
      "amount_received": 1000,
      "currency": "usd",
      "status": "succeeded"
    },
    "previous_attributes": {}
  },
  "livemode": false,
  "pending_webhooks": 1,
  "request": { "id": "req_test" },
  "type": "payment_intent.succeeded"
}
""";

    /// <summary>Tạo payload payment_intent.payment_failed.</summary>
    private static string PaymentIntentFailedPayload(string eventId, string paymentIntentId) =>
        $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-09-30.acacia",
  "created": 1710000000,
  "data": {
    "object": {
      "id": "{{paymentIntentId}}",
      "object": "payment_intent",
      "amount": 1500,
      "amount_received": 0,
      "currency": "usd",
      "status": "requires_payment_method"
    },
    "previous_attributes": {}
  },
  "livemode": false,
  "pending_webhooks": 1,
  "request": { "id": "req_test" },
  "type": "payment_intent.payment_failed"
}
""";

    /// <summary>Tạo payload refund.created.</summary>
    private static string RefundCreatedPayload(
        string eventId,
        string refundId,
        string paymentIntentId) =>
        $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-09-30.acacia",
  "created": 1710000000,
  "data": {
    "object": {
      "id": "{{refundId}}",
      "object": "refund",
      "amount": 500,
      "currency": "usd",
      "payment_intent": "{{paymentIntentId}}",
      "status": "succeeded"
    },
    "previous_attributes": {}
  },
  "livemode": false,
  "pending_webhooks": 1,
  "request": { "id": "req_test" },
  "type": "refund.created"
}
""";
}

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StripeWebhooks.Api.Persistence;
using StripeWebhooks.Api.Persistence.Entities;
using StripeWebhooks.Api.Models;
using StripeWebhooks.Tests.Utils;
using Xunit;

namespace StripeWebhooks.Tests.Integration;

/// <summary>
/// Integration tests cho Stripe Webhook API.
///
/// Các nhóm kiểm thử:
/// 1. Webhook hợp lệ
/// 2. Idempotency
/// 3. Xác thực Stripe Signature
/// 4. Payload không hợp lệ
/// 5. PaymentIntent events
/// 6. Refund events
/// 7. Unsupported events
/// 8. Kiểm tra dữ liệu được lưu vào Database
/// </summary>
public class StripeWebhookApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public StripeWebhookApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ============================================================
    // 1. WEBHOOK THÀNH CÔNG
    // ============================================================

    /// <summary>
    /// Kiểm tra webhook payment_intent.succeeded hợp lệ.
    ///
    /// Mong đợi:
    /// - API trả HTTP 200.
    /// - Event được lưu vào ProcessedEvents.
    /// - PaymentEvent được lưu vào PaymentEvents.
    /// </summary>
    [Fact]
    public async Task Webhook_ValidPaymentSucceeded_Returns200_AndPersists()
    {
        var client = _factory.CreateClient();

        const string eventId = "evt_valid_payment_succeeded";
        const string paymentIntentId = "pi_valid_payment_succeeded";

        var payload = PaymentIntentSucceededPayload(
            eventId,
            paymentIntentId,
            1000,
            "usd");

        var signature = CreateValidSignature(payload);

        var response = await PostWebhookAsync(
            client,
            payload,
            signature);

        // API phải trả HTTP 200
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Event phải được ghi nhận là đã xử lý
        db.ProcessedEvents
            .Any(x => x.EventId == eventId)
            .Should()
            .BeTrue();

        // PaymentEvent phải được lưu
        var paymentEvent = db.PaymentEvents
            .SingleOrDefault(x => x.StripeEventId == eventId);

        paymentEvent.Should().NotBeNull();

        paymentEvent!.Kind
            .Should()
            .Be("payment_intent.succeeded");

        paymentEvent.PaymentIntentId
            .Should()
            .Be(paymentIntentId);

        paymentEvent.Amount
            .Should()
            .Be(1000);

        paymentEvent.Currency
            .Should()
            .Be("usd");
    }


    // ============================================================
    // 2. IDEMPOTENCY
    // ============================================================

    /// <summary>
    /// Kiểm tra cùng một Stripe event được gửi nhiều lần
    /// thì hệ thống chỉ xử lý một lần.
    ///
    /// Đây là cơ chế idempotency.
    /// </summary>
    [Fact]
    public async Task Webhook_DuplicateEvent_IsProcessedOnlyOnce()
    {
        var client = _factory.CreateClient();

        const string eventId = "evt_idempotency_test";
        const string paymentIntentId = "pi_idempotency_test";

        var payload = PaymentIntentSucceededPayload(
            eventId,
            paymentIntentId,
            2000,
            "usd");

        var signature = CreateValidSignature(payload);

        // --------------------------------------------------------
        // Lần gửi thứ nhất
        // --------------------------------------------------------

        var response1 = await PostWebhookAsync(
            client,
            payload,
            signature);

        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        // --------------------------------------------------------
        // Lần gửi thứ hai - cùng event
        // --------------------------------------------------------

        var response2 = await PostWebhookAsync(
            client,
            payload,
            signature);

        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        // --------------------------------------------------------
        // Kiểm tra Database
        // --------------------------------------------------------

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // ProcessedEvents chỉ được có 1 record
        db.ProcessedEvents
            .Count(x => x.EventId == eventId)
            .Should()
            .Be(1);

        // PaymentEvents chỉ được có 1 record
        db.PaymentEvents
            .Count(x => x.StripeEventId == eventId)
            .Should()
            .Be(1);
    }


    // ============================================================
    // 3. STRIPE SIGNATURE
    // ============================================================

    /// <summary>
    /// Không gửi Stripe-Signature.
    ///
    /// Mong đợi:
    /// HTTP 400 BadRequest.
    /// </summary>
    [Fact]
    public async Task Webhook_MissingSignature_Returns400()
    {
        var client = _factory.CreateClient();

        const string eventId = "evt_missing_signature";

        var payload = PaymentIntentSucceededPayload(
            eventId,
            "pi_missing_signature",
            1000,
            "usd");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/webhooks/stripe")
        {
            Content = new StringContent(
                payload,
                Encoding.UTF8,
                "application/json")
        };

        // Cố tình KHÔNG thêm Stripe-Signature

        var response = await client.SendAsync(request);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.BadRequest);
    }


    /// <summary>
    /// Gửi Stripe-Signature không hợp lệ.
    ///
    /// Mong đợi:
    /// - HTTP 400.
    /// - Event không được lưu vào ProcessedEvents.
    /// </summary>
    [Fact]
    public async Task Webhook_InvalidSignature_Returns400_AndDoesNotPersist()
    {
        var client = _factory.CreateClient();

        const string eventId = "evt_invalid_signature";

        var payload = PaymentIntentSucceededPayload(
            eventId,
            "pi_invalid_signature",
            1000,
            "usd");

        // Signature giả
        const string invalidSignature =
            "t=1710000000,v1=deadbeef";

        var response = await PostWebhookAsync(
            client,
            payload,
            invalidSignature);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Event không được ghi nhận
        db.ProcessedEvents
            .Any(x => x.EventId == eventId)
            .Should()
            .BeFalse();

        // Không được tạo PaymentEvent
        db.PaymentEvents
            .Any(x => x.StripeEventId == eventId)
            .Should()
            .BeFalse();
    }


    // ============================================================
    // 4. PAYLOAD VALIDATION
    // ============================================================

    /// <summary>
    /// Kiểm tra JSON payload bị lỗi cú pháp.
    ///
    /// Mong đợi:
    /// HTTP 400 BadRequest.
    /// </summary>
    [Fact]
    public async Task Webhook_MalformedJson_Returns400()
    {
        var client = _factory.CreateClient();

        const string payload =
            "{ this is not valid json";

        var signature = CreateValidSignature(payload);

        var response = await PostWebhookAsync(
            client,
            payload,
            signature);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.BadRequest);
    }


    // ============================================================
    // 5. PAYMENT INTENT - SUCCEEDED
    // ============================================================

    /// <summary>
    /// Kiểm tra event:
    /// payment_intent.succeeded
    ///
    /// Kiểm tra các dữ liệu:
    /// - Event type
    /// - PaymentIntent ID
    /// - Amount
    /// - Currency
    /// </summary>
    [Fact]
    public async Task Webhook_PaymentIntentSucceeded_PersistsCorrectData()
    {
        var client = _factory.CreateClient();

        const string eventId =
            "evt_payment_intent_succeeded_test";

        const string paymentIntentId =
            "pi_payment_intent_succeeded_test";

        const long amount = 3000;

        const string currency = "usd";

        var payload = PaymentIntentSucceededPayload(
            eventId,
            paymentIntentId,
            amount,
            currency);

        var signature = CreateValidSignature(payload);

        var response = await PostWebhookAsync(
            client,
            payload,
            signature);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var paymentEvent = db.PaymentEvents
            .SingleOrDefault(x => x.StripeEventId == eventId);

        paymentEvent.Should().NotBeNull();

        paymentEvent!.Kind
            .Should()
            .Be("payment_intent.succeeded");

        paymentEvent.PaymentIntentId
            .Should()
            .Be(paymentIntentId);

        paymentEvent.Amount
            .Should()
            .Be(amount);

        paymentEvent.Currency
            .Should()
            .Be(currency);
    }


    // ============================================================
    // 6. PAYMENT INTENT - PAYMENT FAILED
    // ============================================================

    /// <summary>
    /// Kiểm tra event:
    /// payment_intent.payment_failed
    ///
    /// Event phải được lưu thành PaymentEvent.
    /// </summary>
    [Fact]
    public async Task Webhook_PaymentIntentFailed_PersistsCorrectData()
    {
        var client = _factory.CreateClient();

        const string eventId =
            "evt_payment_intent_failed_test";

        const string paymentIntentId =
            "pi_payment_intent_failed_test";

        const long amount = 1500;

        const string currency = "usd";

        var payload = PaymentIntentFailedPayload(
            eventId,
            paymentIntentId,
            amount,
            currency);

        var signature = CreateValidSignature(payload);

        var response = await PostWebhookAsync(
            client,
            payload,
            signature);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var paymentEvent = db.PaymentEvents
            .SingleOrDefault(x => x.StripeEventId == eventId);

        paymentEvent.Should().NotBeNull();

        paymentEvent!.Kind
            .Should()
            .Be("payment_intent.payment_failed");

        paymentEvent.PaymentIntentId
            .Should()
            .Be(paymentIntentId);

        paymentEvent.Amount
            .Should()
            .Be(amount);

        paymentEvent.Currency
            .Should()
            .Be(currency);
    }


    // ============================================================
    // 7. REFUND.CREATED
    // ============================================================

    /// <summary>
    /// Kiểm tra event:
    /// refund.created
    ///
    /// Kiểm tra:
    /// - Refund event được lưu.
    /// - PaymentIntent được liên kết đúng.
    /// - Amount đúng.
    /// - Currency đúng.
    /// </summary>
    [Fact]
    public async Task Webhook_RefundCreated_PersistsCorrectData()
    {
        var client = _factory.CreateClient();

        const string eventId =
            "evt_refund_created_test";

        const string refundId =
            "re_refund_created_test";

        const string paymentIntentId =
            "pi_refund_created_test";

        const long amount = 500;

        const string currency = "usd";

        var payload = RefundCreatedPayload(
            eventId,
            refundId,
            paymentIntentId,
            amount,
            currency);

        var signature = CreateValidSignature(payload);

        var response = await PostWebhookAsync(
            client,
            payload,
            signature);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var paymentEvent = db.PaymentEvents
            .SingleOrDefault(x => x.StripeEventId == eventId);

        paymentEvent.Should().NotBeNull();

        paymentEvent!.Kind
            .Should()
            .Be("refund.created");

        paymentEvent.PaymentIntentId
            .Should()
            .Be(paymentIntentId);

        paymentEvent.Amount
            .Should()
            .Be(amount);

        paymentEvent.Currency
            .Should()
            .Be(currency);
    }


    // ============================================================
    // 8. UNSUPPORTED EVENT
    // ============================================================

    /// Kiểm tra event Stripe không được hệ thống hỗ trợ.
    ///
    /// Ví dụ:
    /// customer.created
    ///
    /// Mong đợi:
    /// - API trả 200.
    /// - Event được đánh dấu Processed.
    /// - Không tạo PaymentEvent.
    [Fact]
    public async Task Webhook_UnsupportedEvent_Returns200_AndDoesNotCreatePaymentEvent()
    {
        var client = _factory.CreateClient();

        const string eventId =
            "evt_unsupported_test";

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
          "request": {
            "id": "req_test"
          },
          "type": "customer.created"
        }
        """;

        var signature = CreateValidSignature(payload);

        var response = await PostWebhookAsync(
            client,
            payload,
            signature);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Event phải được đánh dấu là đã xử lý
        db.ProcessedEvents
            .Any(x => x.EventId == eventId)
            .Should()
            .BeTrue();

        // Nhưng không được tạo PaymentEvent
        db.PaymentEvents
            .Any(x => x.StripeEventId == eventId)
            .Should()
            .BeFalse();
    }


    // ============================================================
    // 9. HELPER - TẠO STRIPE SIGNATURE
    // ============================================================

    /// Tạo Stripe-Signature hợp lệ dựa trên payload
    /// và webhook secret của TestWebApplicationFactory.
    private static string CreateValidSignature(string payload)
    {
        return StripeTestSignatures.CreateStripeSignatureHeader(
            payload,
            TestWebApplicationFactory.WebhookSecret);
    }


    // ============================================================
    // 10. HELPER - GỬI WEBHOOK REQUEST
    // ============================================================

    /// Gửi POST request tới /webhooks/stripe.
    ///
    /// Request bao gồm:
    /// - JSON body
    /// - Stripe-Signature
    /// - X-Correlation-Id
    private static async Task<HttpResponseMessage> PostWebhookAsync(
        HttpClient client,
        string payload,
        string signature)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/webhooks/stripe")
        {
            Content = new StringContent(
                payload,
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.TryAddWithoutValidation(
            "Stripe-Signature",
            signature);

        request.Headers.TryAddWithoutValidation(
            "X-Correlation-Id",
            Guid.NewGuid().ToString("N"));

        return await client.SendAsync(request);
    }


    // ============================================================
    // 11. HELPER - PAYMENT_INTENT.SUCCEEDED PAYLOAD
    // ============================================================

    /// Tạo payload Stripe:
    /// payment_intent.succeeded
    private static string PaymentIntentSucceededPayload(
        string eventId,
        string paymentIntentId,
        long amount,
        string currency)
    {
        return $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2024-09-30.acacia",
          "created": 1710000000,
          "data": {
            "object": {
              "id": "{{paymentIntentId}}",
              "object": "payment_intent",
              "amount": {{amount}},
              "amount_received": {{amount}},
              "currency": "{{currency}}",
              "status": "succeeded"
            },
            "previous_attributes": {}
          },
          "livemode": false,
          "pending_webhooks": 1,
          "request": {
            "id": "req_test"
          },
          "type": "payment_intent.succeeded"
        }
        """;
    }


    // ============================================================
    // 12. HELPER - PAYMENT_INTENT.PAYMENT_FAILED PAYLOAD
    // ============================================================

    /// Tạo payload Stripe:
    /// payment_intent.payment_failed
    private static string PaymentIntentFailedPayload(
        string eventId,
        string paymentIntentId,
        long amount,
        string currency)
    {
        return $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2024-09-30.acacia",
          "created": 1710000000,
          "data": {
            "object": {
              "id": "{{paymentIntentId}}",
              "object": "payment_intent",
              "amount": {{amount}},
              "amount_received": 0,
              "currency": "{{currency}}",
              "status": "requires_payment_method"
            },
            "previous_attributes": {}
          },
          "livemode": false,
          "pending_webhooks": 1,
          "request": {
            "id": "req_test"
          },
          "type": "payment_intent.payment_failed"
        }
        """;
    }


    // ============================================================
    // 13. HELPER - REFUND.CREATED PAYLOAD
    // ============================================================

    /// Tạo payload Stripe:
    /// refund.created
    private static string RefundCreatedPayload(
        string eventId,
        string refundId,
        string paymentIntentId,
        long amount,
        string currency)
    {
        return $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2024-09-30.acacia",
          "created": 1710000000,
          "data": {
            "object": {
              "id": "{{refundId}}",
              "object": "refund",
              "amount": {{amount}},
              "currency": "{{currency}}",
              "payment_intent": "{{paymentIntentId}}",
              "status": "succeeded"
            },
            "previous_attributes": {}
          },
          "livemode": false,
          "pending_webhooks": 1,
          "request": {
            "id": "req_test"
          },
          "type": "refund.created"
        }
        """;
    }
}

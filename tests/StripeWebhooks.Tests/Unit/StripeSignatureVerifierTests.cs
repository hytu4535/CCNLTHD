using FluentAssertions;
using Microsoft.Extensions.Configuration;
using StripeWebhooks.Api.Stripe;
using StripeWebhooks.Tests.Utils;
using Xunit;

namespace StripeWebhooks.Tests.Unit;

public class StripeSignatureVerifierTests
{
    private const string Secret = "whsec_test_123";

    // IMPORTANT:
    // Stripe's stripe-dotnet EventConverter can throw NullReferenceException if the
    // JSON isn't "Stripe-realistic" enough, even if it's valid JSON.
    // This fixture is intentionally close to real Stripe webhook payload shape.
    private const string ValidEventPayload = """
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
      "amount_capturable": 0,
      "amount_received": 1000,
      "application": null,
      "application_fee_amount": null,
      "automatic_payment_methods": null,
      "canceled_at": null,
      "cancellation_reason": null,
      "capture_method": "automatic",
      "client_secret": "pi_123_secret_test",
      "confirmation_method": "automatic",
      "created": 1710000000,
      "currency": "usd",
      "customer": null,
      "description": null,
      "invoice": null,
      "last_payment_error": null,
      "latest_charge": "ch_123",
      "livemode": false,
      "metadata": {},
      "next_action": null,
      "on_behalf_of": null,
      "payment_method": "pm_123",
      "payment_method_configuration_details": null,
      "payment_method_options": {},
      "payment_method_types": ["card"],
      "processing": null,
      "receipt_email": null,
      "review": null,
      "setup_future_usage": null,
      "shipping": null,
      "source": null,
      "statement_descriptor": null,
      "statement_descriptor_suffix": null,
      "status": "succeeded",
      "transfer_data": null,
      "transfer_group": null
    },
    "previous_attributes": {}
  },
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_123",
    "idempotency_key": "idem_123"
  },
  "type": "payment_intent.succeeded"
}
""";

    private static StripeSignatureVerifier CreateVerifier(string webhookSecret)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:WebhookSecret"] = webhookSecret
            })
            .Build();

        return new StripeSignatureVerifier(cfg);
    }

    [Fact]
    public void ConstructEvent_ReturnsEvent_WhenSignatureValid()
    {
        // Arrange
        var verifier = CreateVerifier(Secret);
        var header = StripeTestSignatures.CreateStripeSignatureHeader(ValidEventPayload, Secret);

        // Act
        var ev = verifier.ConstructEvent(ValidEventPayload, header);

        // Assert
        ev.Id.Should().Be("evt_123");
        ev.Type.Should().Be("payment_intent.succeeded");
        ev.Data.Object.Should().NotBeNull();
    }

    [Fact]
    public void ConstructEvent_Throws_WhenSignatureInvalid()
    {
        // Arrange
        var verifier = CreateVerifier(Secret);

        // Valid payload, invalid signature header => should throw (signature validation)
        var badHeader = "t=1,v1=deadbeef";

        // Act
        var act = () => verifier.ConstructEvent(ValidEventPayload, badHeader);

        // Assert
        act.Should().Throw<Exception>();
        // If you want to tighten later (once you confirm the exact type):
        // act.Should().Throw<Stripe.StripeException>();
    }

    [Fact]
    public void ConstructEvent_Throws_WhenSecretMissing()
    {
        // Arrange
        var verifier = CreateVerifier("");

        var header = StripeTestSignatures.CreateStripeSignatureHeader(ValidEventPayload, Secret);

        // Act
        var act = () => verifier.ConstructEvent(ValidEventPayload, header);

        // Assert
        act.Should().Throw<Exception>();
    }
}

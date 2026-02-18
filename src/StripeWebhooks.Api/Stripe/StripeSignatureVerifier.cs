using Stripe;

namespace StripeWebhooks.Api.Stripe;

public sealed class StripeSignatureVerifier
{
    private readonly IConfiguration _cfg;

    public StripeSignatureVerifier(IConfiguration cfg)
    {
        _cfg = cfg;
    }

    public Event ConstructEvent(string json, string stripeSignatureHeader)
    {
        var webhookSecret = _cfg["Stripe:WebhookSecret"];

        if (string.IsNullOrWhiteSpace(webhookSecret))
            throw new StripeException("Stripe webhook secret is not configured (Stripe:WebhookSecret).");

        // ✅ Trim to avoid invisible whitespace issues
        webhookSecret = webhookSecret.Trim();

        // ✅ Validate signature, but do NOT fail purely due to API version mismatch
        // (Stripe CLI/account may be on a newer API version than this Stripe.net package expects)
        return EventUtility.ConstructEvent(
            json,
            stripeSignatureHeader,
            webhookSecret,
            tolerance: 300,
            throwOnApiVersionMismatch: false
        );
    }
}

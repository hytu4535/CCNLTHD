using Microsoft.EntityFrameworkCore;
using Stripe;
using StripeWebhooks.Api.Persistence;
using StripeWebhooks.Api.Persistence.Entities;

namespace StripeWebhooks.Api.Stripe;

public sealed class StripeWebhookHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<StripeWebhookHandler> _log;
    private readonly StripeSignatureVerifier _verifier;

    public StripeWebhookHandler(AppDbContext db, ILogger<StripeWebhookHandler> log, StripeSignatureVerifier verifier)
    {
        _db = db;
        _log = log;
        _verifier = verifier;
    }

    public async Task<StripeWebhookResult> HandleAsync(string json, string sigHeader, CancellationToken ct)
    {
        Event stripeEvent;

        try
        {
            stripeEvent = _verifier.ConstructEvent(json, sigHeader);
        }
        catch (StripeException ex)
        {
            _log.LogWarning(ex, "Stripe event construction failed");

            if (ex.Message.Contains("expects API version", StringComparison.OrdinalIgnoreCase))
                return StripeWebhookResult.BadRequest("Stripe API version mismatch (webhook endpoint/CLI vs Stripe.net).");

            if (ex.Message.Contains("secret", StringComparison.OrdinalIgnoreCase))
                return StripeWebhookResult.BadRequest("Stripe webhook secret missing or invalid configuration.");

            return StripeWebhookResult.BadRequest("Invalid Stripe signature or webhook secret.");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to parse or construct Stripe event");
            return StripeWebhookResult.BadRequest("Malformed Stripe webhook payload.");
        }

        // Defensive: Stripe should always provide Id and Type
        if (string.IsNullOrWhiteSpace(stripeEvent.Id) || string.IsNullOrWhiteSpace(stripeEvent.Type))
        {
            _log.LogWarning(
                "Stripe event missing required fields. EventId={EventId} Type={Type}. Returning 200 to avoid retries.",
                stripeEvent.Id, stripeEvent.Type);

            return StripeWebhookResult.Ok;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Race-safe idempotency:
        // - AppDbContext config should enforce UNIQUE index on processed_events.event_id
        // - We insert the processed_events row first and catch DbUpdateException if it's a duplicate.
        try
        {
            _db.ProcessedEvents.Add(new ProcessedEvent
            {
                EventId = stripeEvent.Id,
                EventType = stripeEvent.Type,
                ProcessedAt = DateTimeOffset.UtcNow
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _log.LogInformation("Webhook already processed. EventId={EventId} Type={Type}", stripeEvent.Id, stripeEvent.Type);
            return StripeWebhookResult.Ok;
        }

        try
        {
            await DispatchAsync(stripeEvent, ct);
            await tx.CommitAsync(ct);

            _log.LogInformation("Webhook processed. EventId={EventId} Type={Type}", stripeEvent.Id, stripeEvent.Type);
            return StripeWebhookResult.Ok;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Webhook processing failed. EventId={EventId} Type={Type}", stripeEvent.Id, stripeEvent.Type);
            await tx.RollbackAsync(ct);

            // IMPORTANT: Return 500 so Stripe will retry on transient failures (db/network/etc).
            return StripeWebhookResult.InternalError("Webhook handler failed (transient).");
        }
    }

    private Task DispatchAsync(Event stripeEvent, CancellationToken ct)
    {
        return stripeEvent.Type switch
        {
            "payment_intent.succeeded" => HandlePaymentIntentSucceeded(stripeEvent, ct),
            "payment_intent.payment_failed" => HandlePaymentIntentFailed(stripeEvent, ct),

            "refund.created" => HandleRefundEvent(stripeEvent, ct),
            "refund.updated" => HandleRefundEvent(stripeEvent, ct),

            "charge.refunded" => HandleChargeRefunded(stripeEvent, ct),

            _ => Task.CompletedTask
        };
    }

    private static DateTimeOffset ToOccurredAt(Event stripeEvent)
    {
        // Stripe.net versions differ:
        // - some expose Event.Created as Unix seconds (long)
        // - newer versions often expose it as DateTime / DateTime?
        var createdProp = stripeEvent.Created;

        // If it's already DateTime (most likely in your setup):
        // - treat it as UTC if Kind is Unspecified (Stripe times are UTC)
        if (createdProp is DateTime dt)
        {
            var utc = dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            };

            return new DateTimeOffset(utc);
        }

        // Fallback (shouldn't hit in your current SDK, but safe):
        return DateTimeOffset.UtcNow;
    }


    private async Task HandlePaymentIntentSucceeded(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not PaymentIntent pi)
        {
            _log.LogWarning("payment_intent.succeeded payload was not PaymentIntent. EventId={EventId}", stripeEvent.Id);
            return;
        }

        _log.LogInformation(
            "PaymentIntent succeeded. PaymentIntentId={PaymentIntentId} AmountReceived={Amount} Currency={Currency}",
            pi.Id, pi.AmountReceived, pi.Currency);

        _db.PaymentEvents.Add(new PaymentEvent
        {
            StripeEventId = stripeEvent.Id,
            Kind = stripeEvent.Type,
            PaymentIntentId = pi.Id,
            Amount = pi.AmountReceived,
            Currency = pi.Currency,
            OccurredAt = ToOccurredAt(stripeEvent)
        });

        await _db.SaveChangesAsync(ct);
    }

    private async Task HandlePaymentIntentFailed(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not PaymentIntent pi)
        {
            _log.LogWarning("payment_intent.payment_failed payload was not PaymentIntent. EventId={EventId}", stripeEvent.Id);
            return;
        }

        _log.LogInformation(
            "PaymentIntent failed. PaymentIntentId={PaymentIntentId} Status={Status}",
            pi.Id, pi.Status);

        _db.PaymentEvents.Add(new PaymentEvent
        {
            StripeEventId = stripeEvent.Id,
            Kind = stripeEvent.Type,
            PaymentIntentId = pi.Id,
            Amount = pi.Amount,
            Currency = pi.Currency,
            OccurredAt = ToOccurredAt(stripeEvent)
        });

        await _db.SaveChangesAsync(ct);
    }

    private async Task HandleRefundEvent(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Refund refund)
        {
            _log.LogWarning("{EventType} payload was not Refund. EventId={EventId}", stripeEvent.Type, stripeEvent.Id);
            return;
        }

        _log.LogInformation(
            "Refund event. Type={Type} RefundId={RefundId} Status={Status} PaymentIntentId={PaymentIntentId} Amount={Amount} Currency={Currency}",
            stripeEvent.Type,
            refund.Id,
            refund.Status,
            refund.PaymentIntentId,
            refund.Amount,
            refund.Currency);

        _db.PaymentEvents.Add(new PaymentEvent
        {
            StripeEventId = stripeEvent.Id,
            Kind = stripeEvent.Type,
            PaymentIntentId = refund.PaymentIntentId,
            Amount = refund.Amount,
            Currency = refund.Currency,
            OccurredAt = ToOccurredAt(stripeEvent)
        });

        await _db.SaveChangesAsync(ct);
    }

    private async Task HandleChargeRefunded(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Charge charge)
        {
            _log.LogWarning("charge.refunded payload was not Charge. EventId={EventId}", stripeEvent.Id);
            return;
        }

        var piId = charge.PaymentIntentId;

        _log.LogInformation(
            "Charge refunded. ChargeId={ChargeId} PaymentIntentId={PaymentIntentId} AmountRefunded={AmountRefunded} Currency={Currency}",
            charge.Id,
            piId,
            charge.AmountRefunded,
            charge.Currency);

        _db.PaymentEvents.Add(new PaymentEvent
        {
            StripeEventId = stripeEvent.Id,
            Kind = stripeEvent.Type,
            PaymentIntentId = piId,
            Amount = charge.AmountRefunded,
            Currency = charge.Currency,
            OccurredAt = ToOccurredAt(stripeEvent)
        });

        await _db.SaveChangesAsync(ct);
    }
}

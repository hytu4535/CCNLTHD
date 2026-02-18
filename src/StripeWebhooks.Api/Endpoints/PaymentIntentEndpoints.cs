using Microsoft.EntityFrameworkCore;
using Stripe;

namespace StripeWebhooks.Api.Endpoints;

public record CreatePaymentIntentRequest(long Amount, string Currency);

public record ConfirmPaymentIntentRequest(string? PaymentMethod);

public record RefundPaymentIntentRequest(
    long? Amount,     // minor units; null = full refund
    string? Reason    // optional: "duplicate", "fraudulent", "requested_by_customer"
);

public static class PaymentIntentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentIntentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/demo");

        // POST /demo/payment-intents
        // Optional headers:
        //   Idempotency-Key: <string>  (recommended)
        group.MapPost("/payment-intents", async (
            HttpRequest http,
            CreatePaymentIntentRequest req,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("PaymentIntentEndpoints");

            if (req.Amount <= 0)
                return Results.BadRequest(new { error = "Amount must be > 0 (minor units, e.g. cents)" });

            if (string.IsNullOrWhiteSpace(req.Currency))
                return Results.BadRequest(new { error = "Currency is required (e.g. 'nzd')" });

            var currency = req.Currency.Trim().ToLowerInvariant();
            if (currency.Length != 3)
                return Results.BadRequest(new { error = "Currency must be a 3-letter ISO code (e.g. 'nzd')" });

            var idempotencyKey = http.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                idempotencyKey = $"demo_pi_{Guid.NewGuid():N}";

            log.LogInformation(
                "Creating PaymentIntent. Amount={Amount} Currency={Currency} IdempotencyKey={IdemKey}",
                req.Amount, currency, idempotencyKey);

            try
            {
                var service = new PaymentIntentService();

                var options = new PaymentIntentCreateOptions
                {
                    Amount = req.Amount,
                    Currency = currency,
                    AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                    {
                        Enabled = true,
                        AllowRedirects = "never"
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["demo_repo"] = "stripe-webhooks-dotnet",
                        ["environment"] = "local",
                        ["idempotency_key"] = idempotencyKey
                    }
                };

                var pi = await service.CreateAsync(
                    options,
                    new RequestOptions { IdempotencyKey = idempotencyKey },
                    cancellationToken: ct);

                log.LogInformation(
                    "PaymentIntent created. Id={PaymentIntentId} Status={Status}",
                    pi.Id, pi.Status);

                return Results.Ok(new
                {
                    pi.Id,
                    pi.ClientSecret,
                    pi.Status,
                    IdempotencyKey = idempotencyKey
                });
            }
            catch (StripeException sx)
            {
                log.LogError(
                    sx,
                    "Stripe error creating PaymentIntent. Message={Message} Type={Type} Code={Code} Decline={Decline} RequestId={RequestId}",
                    sx.StripeError?.Message ?? sx.Message,
                    sx.StripeError?.Type,
                    sx.StripeError?.Code,
                    sx.StripeError?.DeclineCode,
                    sx.StripeResponse?.RequestId
                );

                return Results.Problem(
                    title: "Stripe error creating PaymentIntent",
                    detail: sx.StripeError?.Message ?? sx.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // POST /demo/payment-intents/{id}/confirm
        group.MapPost("/payment-intents/{id}/confirm", async (
            string id,
            HttpRequest http,
            ConfirmPaymentIntentRequest? body,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("PaymentIntentEndpoints");

            var paymentMethod =
                body?.PaymentMethod
                ?? http.Query["paymentMethod"].FirstOrDefault()
                ?? http.Headers["X-Test-PaymentMethod"].FirstOrDefault()
                ?? "pm_card_visa";

            if (string.IsNullOrWhiteSpace(paymentMethod))
                return Results.BadRequest(new { error = "paymentMethod is required" });

            var confirmIdempotencyKey = http.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(confirmIdempotencyKey))
                confirmIdempotencyKey = $"demo_pi_confirm_{id}_{Guid.NewGuid():N}";

            log.LogInformation(
                "Confirming PaymentIntent. Id={PaymentIntentId} PaymentMethod={PaymentMethod} IdempotencyKey={IdemKey}",
                id, paymentMethod, confirmIdempotencyKey);

            try
            {
                var service = new PaymentIntentService();

                var pi = await service.ConfirmAsync(
                    id,
                    new PaymentIntentConfirmOptions { PaymentMethod = paymentMethod },
                    requestOptions: new RequestOptions { IdempotencyKey = confirmIdempotencyKey },
                    cancellationToken: ct);

                log.LogInformation(
                    "PaymentIntent confirmed. Id={PaymentIntentId} Status={Status}",
                    pi.Id, pi.Status);

                return Results.Ok(new
                {
                    pi.Id,
                    pi.Status,
                    IdempotencyKey = confirmIdempotencyKey
                });
            }
            catch (StripeException sx)
            {
                log.LogError(
                    sx,
                    "Stripe error confirming PaymentIntent. Id={PaymentIntentId} Message={Message} Type={Type} Code={Code} Decline={Decline} RequestId={RequestId}",
                    id,
                    sx.StripeError?.Message ?? sx.Message,
                    sx.StripeError?.Type,
                    sx.StripeError?.Code,
                    sx.StripeError?.DeclineCode,
                    sx.StripeResponse?.RequestId
                );

                return Results.Problem(
                    title: "Stripe error confirming PaymentIntent",
                    detail: sx.StripeError?.Message ?? sx.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // POST /demo/payment-intents/{id}/refund
        group.MapPost("/payment-intents/{id}/refund", async (
            string id,
            HttpRequest http,
            RefundPaymentIntentRequest? body,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("PaymentIntentEndpoints");

            long? amount =
                body?.Amount
                ?? (long.TryParse(http.Query["amount"].FirstOrDefault(), out var qAmt) ? qAmt : null);

            var reason =
                body?.Reason
                ?? http.Query["reason"].FirstOrDefault();

            if (amount is <= 0)
                return Results.BadRequest(new { error = "amount must be > 0 when provided (minor units). Omit for full refund." });

            string? mappedReason = MapRefundReason(reason);

            var idem = http.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(idem))
                idem = $"demo_refund_{id}_{Guid.NewGuid():N}";

            log.LogInformation(
                "Creating refund. PaymentIntentId={PaymentIntentId} Amount={Amount} Reason={Reason} IdempotencyKey={IdemKey}",
                id, amount?.ToString() ?? "<full>", mappedReason ?? "<none>", idem);

            try
            {
                var refundService = new RefundService();

                var options = new RefundCreateOptions
                {
                    PaymentIntent = id,
                    Amount = amount, // null => full refund
                    Reason = mappedReason,
                    Metadata = new Dictionary<string, string>
                    {
                        ["demo_repo"] = "stripe-webhooks-dotnet",
                        ["environment"] = "local",
                        ["idempotency_key"] = idem,
                        ["flow"] = "refund"
                    }
                };

                var refund = await refundService.CreateAsync(
                    options,
                    new RequestOptions { IdempotencyKey = idem },
                    cancellationToken: ct);

                log.LogInformation(
                    "Refund created. RefundId={RefundId} Status={Status} PaymentIntentId={PaymentIntentId} Amount={Amount} Currency={Currency}",
                    refund.Id, refund.Status, refund.PaymentIntentId, refund.Amount, refund.Currency);

                return Results.Ok(new
                {
                    refund.Id,
                    refund.Status,
                    refund.PaymentIntentId,
                    refund.Amount,
                    refund.Currency,
                    IdempotencyKey = idem
                });
            }
            catch (StripeException sx)
            {
                log.LogError(
                    sx,
                    "Stripe error creating refund. PaymentIntentId={PaymentIntentId} Message={Message} Type={Type} Code={Code} Decline={Decline} RequestId={RequestId}",
                    id,
                    sx.StripeError?.Message ?? sx.Message,
                    sx.StripeError?.Type,
                    sx.StripeError?.Code,
                    sx.StripeError?.DeclineCode,
                    sx.StripeResponse?.RequestId
                );

                return Results.Problem(
                    title: "Stripe error creating refund",
                    detail: sx.StripeError?.Message ?? sx.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // GET /demo/payment-events?take=50
        group.MapGet("/payment-events", async (
            Persistence.AppDbContext db,
            ILoggerFactory loggerFactory,
            int? take,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("PaymentIntentEndpoints");

            var n = take.GetValueOrDefault(50);
            n = Math.Clamp(n, 1, 200);

            var events = await db.PaymentEvents
                .OrderByDescending(x => x.OccurredAt)
                .Take(n)
                .ToListAsync(ct);

            log.LogInformation("Returning {Count} payment_events rows", events.Count);

            return Results.Ok(events);
        });

        return app;
    }

    private static string? MapRefundReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;

        var r = reason.Trim().ToLowerInvariant();

        // Stripe accepts: duplicate, fraudulent, requested_by_customer
        return r switch
        {
            "duplicate" => "duplicate",
            "fraud" or "fraudulent" => "fraudulent",
            "customer" or "requested_by_customer" => "requested_by_customer",
            _ => null
        };
    }
}

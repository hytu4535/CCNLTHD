using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using StripeWebhooks.Api.Infrastructure;
using StripeWebhooks.Api.Stripe;

namespace StripeWebhooks.Api.Endpoints;

public static class StripeWebhookEndpoints
{
    public static IEndpointRouteBuilder MapStripeWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/stripe", async (
            HttpRequest request,
            StripeWebhookHandler handler,
            ILogger<StripeWebhookHandler> log,
            CancellationToken ct) =>
        {
            var sw = Stopwatch.StartNew();

            var correlationId = request.HttpContext.Items[CorrelationIdMiddleware.HeaderName]?.ToString();
            var signatureHeader = request.Headers["Stripe-Signature"].FirstOrDefault();
            var hasSig = !string.IsNullOrWhiteSpace(signatureHeader);

            log.LogInformation(
                "Stripe webhook received. CorrelationId={CorrelationId} HasSignature={HasSig} ContentLength={Len} UA={UA}",
                correlationId,
                hasSig,
                request.ContentLength,
                request.Headers.UserAgent.ToString());

            if (!hasSig)
            {
                log.LogWarning("Stripe webhook rejected: missing Stripe-Signature header. CorrelationId={CorrelationId}", correlationId);
                return Results.BadRequest(new { error = "Missing Stripe-Signature header" });
            }

            // Read RAW body exactly (UTF-8) for signature verification
            string json;
            try
            {
                request.EnableBuffering();
                json = await new StreamReader(request.Body, Encoding.UTF8).ReadToEndAsync(ct);
                request.Body.Position = 0;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed reading Stripe webhook request body. CorrelationId={CorrelationId}", correlationId);
                return Results.BadRequest(new { error = "Failed to read request body" });
            }

            var result = await handler.HandleAsync(json, signatureHeader!, ct);

            sw.Stop();

            return result switch
            {
                StripeWebhookResult.OkResult =>
                    LogAndReturnOk(log, correlationId, sw.ElapsedMilliseconds),

                StripeWebhookResult.BadRequestResult br =>
                    LogAndReturnBadRequest(log, correlationId, br.Message, sw.ElapsedMilliseconds),

                StripeWebhookResult.UnauthorizedResult =>
                    LogAndReturnUnauthorized(log, correlationId, sw.ElapsedMilliseconds),

                StripeWebhookResult.InternalErrorResult ie =>
                    LogAndReturnInternalError(log, correlationId, ie.Message, sw.ElapsedMilliseconds),

                _ => LogAndReturnOk(log, correlationId, sw.ElapsedMilliseconds)
            };
        });

        return app;
    }

    private static IResult LogAndReturnOk(ILogger log, string? correlationId, long elapsedMs)
    {
        log.LogInformation("Stripe webhook handled. Status=200 CorrelationId={CorrelationId} ElapsedMs={ElapsedMs}", correlationId, elapsedMs);
        return Results.Ok();
    }

    private static IResult LogAndReturnBadRequest(ILogger log, string? correlationId, string message, long elapsedMs)
    {
        log.LogWarning("Stripe webhook rejected. Status=400 CorrelationId={CorrelationId} Reason={Reason} ElapsedMs={ElapsedMs}",
            correlationId, message, elapsedMs);
        return Results.BadRequest(new { error = message });
    }

    private static IResult LogAndReturnUnauthorized(ILogger log, string? correlationId, long elapsedMs)
    {
        log.LogWarning("Stripe webhook unauthorized. Status=401 CorrelationId={CorrelationId} ElapsedMs={ElapsedMs}", correlationId, elapsedMs);
        return Results.Unauthorized();
    }

    private static IResult LogAndReturnInternalError(ILogger log, string? correlationId, string message, long elapsedMs)
    {
        log.LogError("Stripe webhook failed. Status=500 CorrelationId={CorrelationId} Reason={Reason} ElapsedMs={ElapsedMs}",
            correlationId, message, elapsedMs);

        return Results.Problem(
            title: "Webhook processing failed",
            detail: message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
}

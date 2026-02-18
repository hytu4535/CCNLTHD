namespace StripeWebhooks.Api.Stripe;

public abstract record StripeWebhookResult
{
    public sealed record OkResult : StripeWebhookResult;
    public sealed record BadRequestResult(string Message) : StripeWebhookResult;
    public sealed record UnauthorizedResult : StripeWebhookResult;
    public sealed record InternalErrorResult(string Message) : StripeWebhookResult;

    public static readonly OkResult Ok = new();
    public static readonly UnauthorizedResult Unauthorized = new();

    public static StripeWebhookResult BadRequest(string message) => new BadRequestResult(message);
    public static StripeWebhookResult InternalError(string message) => new InternalErrorResult(message);
}

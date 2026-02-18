namespace StripeWebhooks.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", () => Results.Ok(new { name = "stripe-webhooks-dotnet", status = "ok" }));
        app.MapHealthChecks("/health");
        return app;
    }
}

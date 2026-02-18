using System.Diagnostics;

namespace StripeWebhooks.Api.Infrastructure;

public sealed class CorrelationIdMiddleware : IMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly ILogger<CorrelationIdMiddleware> _log;

    public CorrelationIdMiddleware(ILogger<CorrelationIdMiddleware> log) => _log = log;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var incoming = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = string.IsNullOrWhiteSpace(incoming)
            ? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
            : incoming.Trim();

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_log.BeginScope(new Dictionary<string, object>
               {
                   ["CorrelationId"] = correlationId
               }))
        {
            await next(context);
        }
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IServiceCollection AddCorrelationId(this IServiceCollection services)
        => services.AddTransient<CorrelationIdMiddleware>();

    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();
}

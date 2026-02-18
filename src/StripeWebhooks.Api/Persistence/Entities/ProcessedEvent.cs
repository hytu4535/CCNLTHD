namespace StripeWebhooks.Api.Persistence.Entities;

public sealed class ProcessedEvent
{
    public string EventId { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}

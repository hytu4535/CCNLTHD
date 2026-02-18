namespace StripeWebhooks.Api.Persistence.Entities;

public sealed class PaymentEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StripeEventId { get; set; } = default!;
    public string Kind { get; set; } = default!;
    public string? PaymentIntentId { get; set; }
    public long? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

namespace StripeWebhooks.Api.Persistence.Entities;

using StripeWebhooks.Api.Models;

public sealed class Order
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string? PaymentIntentId { get; set; }
    public OrderPaymentStatus PaymentStatus { get; set; } = OrderPaymentStatus.Pending;
    public int Quantity { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum OrderPaymentStatus
{
    Pending,
    Paid,
    Failed
}
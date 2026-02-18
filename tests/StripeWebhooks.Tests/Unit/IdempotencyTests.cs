using FluentAssertions;
using StripeWebhooks.Api.Persistence.Entities;
using Xunit;

namespace StripeWebhooks.Tests.Unit;

public class IdempotencyTests
{
    [Fact]
    public void ProcessedEvent_DefaultsProcessedAt()
    {
        var pe = new ProcessedEvent { EventId = "evt_123", EventType = "payment_intent.succeeded" };
        pe.ProcessedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}

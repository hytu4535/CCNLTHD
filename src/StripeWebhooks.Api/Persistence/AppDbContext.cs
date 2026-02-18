using Microsoft.EntityFrameworkCore;
using StripeWebhooks.Api.Persistence.Entities;

namespace StripeWebhooks.Api.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProcessedEvent>(b =>
        {
            b.ToTable("processed_events");
            b.HasKey(x => x.EventId);

            b.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(128);
            b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(128);
            b.Property(x => x.ProcessedAt).HasColumnName("processed_at");

            // ✅ idempotency race-proofing
            b.HasIndex(x => x.EventId).IsUnique();
        });

        modelBuilder.Entity<PaymentEvent>(b =>
        {
            b.ToTable("payment_events");
            b.HasKey(x => x.Id);

            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.StripeEventId).HasColumnName("stripe_event_id").HasMaxLength(128);
            b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(128);
            b.Property(x => x.PaymentIntentId).HasColumnName("payment_intent_id").HasMaxLength(128);
            b.Property(x => x.Amount).HasColumnName("amount");
            b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(16);
            b.Property(x => x.OccurredAt).HasColumnName("occurred_at");

            b.HasIndex(x => x.StripeEventId).IsUnique();
        });
    }
}

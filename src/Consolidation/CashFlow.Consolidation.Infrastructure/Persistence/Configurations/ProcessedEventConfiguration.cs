using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CashFlow.Consolidation.Infrastructure.Persistence.Configurations;

internal sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("processed_events");

        builder.HasKey(processed => processed.EventId);

        builder.Property(processed => processed.EventId)
            .HasColumnName("event_id")
            .ValueGeneratedNever();

        builder.Property(processed => processed.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(processed => processed.ProcessedAtUtc)
            .HasColumnName("processed_at_utc")
            .IsRequired();

        builder.HasIndex(processed => processed.ProcessedAtUtc)
            .HasDatabaseName("ix_processed_events_processed_at_utc");
    }
}

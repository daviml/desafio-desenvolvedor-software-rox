using CashFlow.Launches.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CashFlow.Launches.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(message => message.Type)
            .HasColumnName("type")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(message => message.Payload)
            .HasColumnName("payload")
            .IsRequired();

        builder.Property(message => message.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .IsRequired();

        builder.Property(message => message.ProcessedAtUtc)
            .HasColumnName("processed_at_utc");

        builder.Property(message => message.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired();

        builder.Property(message => message.NextAttemptAtUtc)
            .HasColumnName("next_attempt_at_utc");

        builder.Property(message => message.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(2000);

        // The dispatcher's only query: pending messages, oldest first. A filtered index keeps it
        // small and constant-cost no matter how much history the table accumulates.
        builder.HasIndex(message => new { message.ProcessedAtUtc, message.NextAttemptAtUtc, message.OccurredAtUtc })
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter(null);
    }
}

using CashFlow.Launches.Domain.Entries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CashFlow.Launches.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping kept out of the aggregate so the domain stays free of persistence attributes.
/// </summary>
internal sealed class EntryConfiguration : IEntityTypeConfiguration<Entry>
{
    public void Configure(EntityTypeBuilder<Entry> builder)
    {
        builder.ToTable("entries");

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new EntryId(value))
            .ValueGeneratedNever();

        builder.Property(entry => entry.MerchantId)
            .HasColumnName("merchant_id")
            .HasConversion(id => id.Value, value => new MerchantId(value))
            .IsRequired();

        builder.Property(entry => entry.Type)
            .HasColumnName("type")
            .HasConversion<int>()
            .IsRequired();

        // Money is a value object: two columns, one concept.
        builder.ComplexProperty(entry => entry.Amount, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("amount")
                .HasPrecision(18, 2)
                .IsRequired();

            money.Property(value => value.Currency)
                .HasColumnName("currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        builder.Property(entry => entry.EntryDate)
            .HasColumnName("entry_date")
            .IsRequired();

        builder.Property(entry => entry.Description)
            .HasColumnName("description")
            .HasMaxLength(Entry.MaxDescriptionLength)
            .IsRequired();

        builder.Property(entry => entry.Category)
            .HasColumnName("category")
            .HasMaxLength(Entry.MaxCategoryLength);

        builder.Property(entry => entry.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(entry => entry.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(Entry.MaxIdempotencyKeyLength);

        builder.Property(entry => entry.RegisteredAtUtc)
            .HasColumnName("registered_at_utc")
            .IsRequired();

        builder.Property(entry => entry.CancelledAtUtc)
            .HasColumnName("cancelled_at_utc");

        builder.Property(entry => entry.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(300);

        builder.Ignore(entry => entry.DomainEvents);
        builder.Ignore(entry => entry.SignedAmount);
        builder.Ignore(entry => entry.IsCancelled);

        // Covers the listing endpoint's access pattern: one merchant, a date range, newest first.
        builder.HasIndex(entry => new { entry.MerchantId, entry.EntryDate })
            .HasDatabaseName("ix_entries_merchant_id_entry_date");

        // Enforces idempotent registration at the only place that can win a race: the database.
        builder.HasIndex(entry => new { entry.MerchantId, entry.IdempotencyKey })
            .HasDatabaseName("ux_entries_merchant_id_idempotency_key")
            .IsUnique()
            .HasFilter(null);
    }
}

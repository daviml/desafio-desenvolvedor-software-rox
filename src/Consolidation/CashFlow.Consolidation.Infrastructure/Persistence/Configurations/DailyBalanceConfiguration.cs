using CashFlow.Consolidation.Domain.DailyBalances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CashFlow.Consolidation.Infrastructure.Persistence.Configurations;

internal sealed class DailyBalanceConfiguration : IEntityTypeConfiguration<DailyBalance>
{
    public void Configure(EntityTypeBuilder<DailyBalance> builder)
    {
        builder.ToTable("daily_balances");

        builder.HasKey(balance => balance.Id);

        builder.Property(balance => balance.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(balance => balance.MerchantId)
            .HasColumnName("merchant_id")
            .HasConversion(id => id.Value, value => new MerchantId(value))
            .IsRequired();

        builder.Property(balance => balance.Date)
            .HasColumnName("date")
            .IsRequired();

        builder.Property(balance => balance.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.ComplexProperty(balance => balance.TotalCredits, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("total_credits")
                .HasPrecision(18, 2)
                .IsRequired();

            money.Property(value => value.Currency)
                .HasColumnName("total_credits_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        builder.ComplexProperty(balance => balance.TotalDebits, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("total_debits")
                .HasPrecision(18, 2)
                .IsRequired();

            money.Property(value => value.Currency)
                .HasColumnName("total_debits_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        builder.Property(balance => balance.CreditCount)
            .HasColumnName("credit_count")
            .IsRequired();

        builder.Property(balance => balance.DebitCount)
            .HasColumnName("debit_count")
            .IsRequired();

        builder.Property(balance => balance.LastUpdatedAtUtc)
            .HasColumnName("last_updated_at_utc")
            .IsRequired();

        // Optimistic concurrency: EF appends the original value to the UPDATE's WHERE clause,
        // so two consumers racing on the same day cannot lose one another's amounts.
        builder.Property(balance => balance.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Ignore(balance => balance.Balance);
        builder.Ignore(balance => balance.EntryCount);
        builder.Ignore(balance => balance.DomainEvents);

        // One row per merchant per day - enforced, not merely assumed by the projector.
        builder.HasIndex(balance => new { balance.MerchantId, balance.Date })
            .HasDatabaseName("ux_daily_balances_merchant_id_date")
            .IsUnique();
    }
}

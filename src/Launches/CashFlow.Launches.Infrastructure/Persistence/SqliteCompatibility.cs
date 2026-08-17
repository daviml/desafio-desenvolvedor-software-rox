using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CashFlow.Launches.Infrastructure.Persistence;

/// <summary>
/// SQLite has no native date/time type: it stores <see cref="DateTimeOffset"/> as text, which makes
/// ordering and range filters untranslatable. Encoding those columns as a sortable integer restores
/// both, so the same LINQ runs unchanged on SQLite (tests, offline profile) and on PostgreSQL.
/// </summary>
internal static class SqliteCompatibility
{
    public static void ApplyDateTimeOffsetConverters(ModelBuilder modelBuilder, DatabaseFacade database)
    {
        if (!database.IsSqlite())
        {
            return;
        }

        var converter = new DateTimeOffsetToBinaryConverter();

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(converter);
                }
            }
        }
    }
}

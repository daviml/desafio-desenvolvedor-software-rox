using CashFlow.Launches.Application.Abstractions;
using CashFlow.Launches.Domain.Entries;
using CashFlow.SharedKernel.Application;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CashFlow.Launches.Infrastructure.Persistence;

/// <summary>
/// Commits the unit of work and translates database-specific failures into application-level
/// exceptions, so no EF Core or Npgsql type ever leaks upwards.
/// </summary>
internal sealed class LaunchesUnitOfWork(LaunchesDbContext context) : IUnitOfWork
{
    private const int SqliteConstraintErrorCode = 19;
    private const int SqliteConstraintUniqueExtendedErrorCode = 2067;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            var idempotencyKey = exception.Entries
                .Select(entry => entry.Entity)
                .OfType<Entry>()
                .Select(entry => entry.IdempotencyKey)
                .FirstOrDefault(key => !string.IsNullOrEmpty(key));

            throw DuplicateIdempotencyKeyException.ForKey(idempotencyKey ?? string.Empty, exception);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        PostgresException postgres => string.Equals(
            postgres.SqlState,
            PostgresErrorCodes.UniqueViolation,
            StringComparison.Ordinal),
        SqliteException sqlite => sqlite.SqliteErrorCode == SqliteConstraintErrorCode
            && sqlite.SqliteExtendedErrorCode == SqliteConstraintUniqueExtendedErrorCode,
        _ => false,
    };
}

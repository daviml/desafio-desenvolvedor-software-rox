using CashFlow.Consolidation.Application.Abstractions;
using CashFlow.SharedKernel.Application;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CashFlow.Consolidation.Infrastructure.Persistence;

/// <summary>
/// Commits the projection update and its deduplication row atomically, translating the two
/// database outcomes the projector cares about into application-level exceptions.
/// </summary>
internal sealed class ConsolidationUnitOfWork(ConsolidationDbContext context) : IUnitOfWork
{
    private const int SqliteConstraintErrorCode = 19;
    private const int SqliteConstraintPrimaryKeyExtendedErrorCode = 1555;
    private const int SqliteConstraintUniqueExtendedErrorCode = 2067;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(
                "The daily balance was modified concurrently; the operation must be retried.",
                exception);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new DuplicateProcessedEventException(
                "This event was already applied by a concurrent consumer.",
                exception);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        PostgresException postgres => string.Equals(
            postgres.SqlState,
            PostgresErrorCodes.UniqueViolation,
            StringComparison.Ordinal),
        SqliteException sqlite => sqlite.SqliteErrorCode == SqliteConstraintErrorCode
            && sqlite.SqliteExtendedErrorCode is SqliteConstraintUniqueExtendedErrorCode
                or SqliteConstraintPrimaryKeyExtendedErrorCode,
        _ => false,
    };
}

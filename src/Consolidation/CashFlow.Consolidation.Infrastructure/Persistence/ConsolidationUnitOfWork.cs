using CashFlow.Consolidation.Application.Abstractions;
using CashFlow.SharedKernel.Application;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CashFlow.Consolidation.Infrastructure.Persistence;

/// <summary>
/// Commits the projection update and its deduplication row atomically, translating database
/// outcomes into the two application-level failures the projector knows how to react to.
/// </summary>
/// <remarks>
/// Telling those two apart is essential and easy to get wrong: a violation on
/// <c>processed_events</c> means the event really was applied before and may be skipped, while a
/// violation on <c>daily_balances</c> means two consumers tried to open the same day at once -
/// a race that must be <em>retried</em>, never skipped, or the amount would be lost.
/// </remarks>
internal sealed class ConsolidationUnitOfWork(ConsolidationDbContext context) : IUnitOfWork
{
    private const int SqliteConstraintErrorCode = 19;
    private const int SqliteBusyErrorCode = 5;
    private const int SqliteLockedErrorCode = 6;

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
        catch (DbUpdateException exception) when (IsDuplicateProcessedEvent(exception))
        {
            throw new DuplicateProcessedEventException(
                "This event was already applied by a concurrent consumer.",
                exception);
        }
        catch (DbUpdateException exception) when (IsTransientConflict(exception))
        {
            throw new ConcurrencyConflictException(
                "A concurrent consumer touched the same daily balance; the operation must be retried.",
                exception);
        }
    }

    /// <summary>True only when the deduplication row itself collided, i.e. a genuine replay.</summary>
    private static bool IsDuplicateProcessedEvent(DbUpdateException exception)
    {
        if (!IsUniqueViolation(exception))
        {
            return false;
        }

        // PostgreSQL names the offending constraint, which is the most precise signal available.
        if (exception.InnerException is PostgresException { ConstraintName: { } constraintName })
        {
            return constraintName.Contains("processed_events", StringComparison.OrdinalIgnoreCase);
        }

        // Otherwise fall back to the entities EF attributed the failing command to.
        return exception.Entries.Any(entry => entry.Entity is ProcessedEvent);
    }

    /// <summary>
    /// Any other write conflict: a duplicate day row from a concurrent "open the day", or SQLite
    /// refusing a busy database. Both are transient and safe to retry on fresh state.
    /// </summary>
    private static bool IsTransientConflict(DbUpdateException exception) =>
        IsUniqueViolation(exception)
        || exception.InnerException is SqliteException
        {
            SqliteErrorCode: SqliteBusyErrorCode or SqliteLockedErrorCode,
        };

    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        PostgresException postgres => string.Equals(
            postgres.SqlState,
            PostgresErrorCodes.UniqueViolation,
            StringComparison.Ordinal),
        SqliteException sqlite => sqlite.SqliteErrorCode == SqliteConstraintErrorCode,
        _ => false,
    };
}

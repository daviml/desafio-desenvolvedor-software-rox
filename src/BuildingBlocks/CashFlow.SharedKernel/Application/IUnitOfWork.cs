namespace CashFlow.SharedKernel.Application;

/// <summary>
/// Commits every change made through the repositories in a single atomic operation.
/// Repositories stay free of persistence timing concerns.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

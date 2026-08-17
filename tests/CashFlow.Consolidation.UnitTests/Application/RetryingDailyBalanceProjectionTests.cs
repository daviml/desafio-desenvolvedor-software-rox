using CashFlow.Consolidation.Application.Abstractions;
using CashFlow.Consolidation.Application.Projection;
using CashFlow.Consolidation.Domain.DailyBalances;
using CashFlow.Consolidation.UnitTests.TestSupport;
using CashFlow.Messaging.Contracts;
using CashFlow.SharedKernel.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CashFlow.Consolidation.UnitTests.Application;

/// <summary>
/// Regression coverage for a race that silently lost money: when two consumers folded entries of
/// the same merchant and day at once, the loser's transaction failed and the event was dropped.
/// A lost race must always be retried, never skipped.
/// </summary>
public sealed class RetryingDailyBalanceProjectionTests
{
    private static readonly Guid MerchantGuid = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly DateOnly Date = new(2026, 3, 15);

    private readonly IDailyBalanceRepository _repository = Substitute.For<IDailyBalanceRepository>();
    private readonly IProcessedEventStore _processedEvents = Substitute.For<IProcessedEventStore>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static EntryRegisteredIntegrationEvent RegisteredEvent() => new()
    {
        EntryId = Guid.NewGuid(),
        MerchantId = MerchantGuid,
        Type = EntryType.Credit,
        Amount = 100m,
        Currency = "BRL",
        EntryDate = Date,
        Description = "Venda",
    };

    private RetryingDailyBalanceProjection CreateProjection()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repository);
        services.AddSingleton(_processedEvents);
        services.AddSingleton(_unitOfWork);
        services.AddSingleton<CashFlow.SharedKernel.Time.IClock>(FixedClock.Default);
        services.AddSingleton<ILogger<DailyBalanceProjector>>(NullLogger<DailyBalanceProjector>.Instance);
        services.AddScoped<DailyBalanceProjector>();

        return new RetryingDailyBalanceProjection(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RetryingDailyBalanceProjection>.Instance);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheFirstAttemptLosesTheRace_RetriesUntilItSucceeds()
    {
        var attempts = 0;

        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<Task<int>>(_ =>
        {
            attempts++;
            return attempts < 3
                ? throw new ConcurrencyConflictException("Two consumers opened the same day.")
                : Task.FromResult(1);
        });

        await CreateProjection().ApplyAsync(RegisteredEvent(), CancellationToken.None);

        attempts.ShouldBe(3);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheRaceNeverResolves_SurfacesTheFailureSoTheMessageIsNotLost()
    {
        _unitOfWork
            .SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new ConcurrencyConflictException("Still contended."));

        // Bubbling up is what makes the broker redeliver or dead-letter the message.
        // Swallowing here would drop the amount from the balance for good.
        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => CreateProjection().ApplyAsync(RegisteredEvent(), CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAsync_AGenuineReplay_IsSkippedWithoutRetrying()
    {
        var attempts = 0;

        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<Task<int>>(_ =>
        {
            attempts++;
            throw new DuplicateProcessedEventException();
        });

        await CreateProjection().ApplyAsync(RegisteredEvent(), CancellationToken.None);

        attempts.ShouldBe(1);
    }
}

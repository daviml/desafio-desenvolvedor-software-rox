using CashFlow.Launches.Domain.Entries;
using CashFlow.Launches.Infrastructure.Persistence;
using CashFlow.Launches.Infrastructure.Persistence.Outbox;
using CashFlow.Messaging;
using CashFlow.SharedKernel.Application;
using CashFlow.SharedKernel.Domain;
using CashFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CashFlow.Launches.IntegrationTests;

/// <summary>
/// Proves the non-functional requirement that matters most: the write path keeps working while the
/// broker is unavailable, and nothing is lost once it comes back.
/// </summary>
public sealed class OutboxDispatcherTests(LaunchesApiFactory factory) : IClassFixture<LaunchesApiFactory>
{
    private async Task<Guid> GivenAnEntryHasBeenRegisteredAsync()
    {
        // Start from a clean outbox so each scenario reasons about exactly one pending message.
        await factory.QueryDatabaseAsync(async context =>
            await context.OutboxMessages.ExecuteDeleteAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var entries = scope.ServiceProvider.GetRequiredService<IEntryRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var entry = Entry.Register(
            MerchantId.New(),
            EntryType.Credit,
            Money.From(123.45m),
            clock.Today,
            "Venda registrada durante indisponibilidade do broker",
            clock);

        entries.Add(entry);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        return entry.Id.Value;
    }

    private Task<OutboxMessage> GetMessageAsync() =>
        factory.QueryDatabaseAsync(context => context.OutboxMessages.AsNoTracking().SingleAsync());

    [Fact]
    public async Task SaveChanges_QueuesTheIntegrationEventAsPending()
    {
        await GivenAnEntryHasBeenRegisteredAsync();

        var pending = await GetMessageAsync();

        pending.ProcessedAtUtc.ShouldBeNull();
        pending.AttemptCount.ShouldBe(0);
        pending.Type.ShouldBe("cashflow.entry.registered");
        pending.Payload.ShouldContain("\"amount\":123.45");
    }

    [Fact]
    public async Task Sweep_WhenTheBrokerIsDown_KeepsTheMessagePendingAndSchedulesARetry()
    {
        await GivenAnEntryHasBeenRegisteredAsync();

        await RunSweepAsync(new FailingPublisher());

        var message = await GetMessageAsync();

        message.ProcessedAtUtc.ShouldBeNull();
        message.AttemptCount.ShouldBe(1);
        message.NextAttemptAtUtc.ShouldNotBeNull();
        message.LastError.ShouldNotBeNull().ShouldContain("broker is down");
    }

    [Fact]
    public async Task Sweep_WhenTheBrokerRecovers_PublishesThePendingMessageExactlyOnce()
    {
        var entryId = await GivenAnEntryHasBeenRegisteredAsync();

        await RunSweepAsync(new FailingPublisher());

        var afterRecovery = new RecordingPublisher();
        await RunSweepAsync(afterRecovery, skipBackoff: true);

        afterRecovery.Published.ShouldHaveSingleItem();

        var message = await GetMessageAsync();
        message.ProcessedAtUtc.ShouldNotBeNull();
        message.LastError.ShouldBeNull();
        message.Payload.ShouldContain(entryId.ToString());

        // A further sweep must not re-publish what the broker already confirmed.
        var secondPass = new RecordingPublisher();
        await RunSweepAsync(secondPass, skipBackoff: true);

        secondPass.Published.ShouldBeEmpty();
    }

    /// <summary>
    /// Runs one dispatcher sweep with a substitute publisher, reusing the application's own scoped
    /// services so the real query, serialisation and update paths are exercised.
    /// </summary>
    private async Task RunSweepAsync(IIntegrationEventPublisher publisher, bool skipBackoff = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        // Moving the clock forward is how a test steps over a retry backoff without sleeping.
        var clock = new OffsetClock(skipBackoff ? TimeSpan.FromHours(1) : TimeSpan.Zero);

        var dispatcher = new OutboxDispatcher(
            provider.GetRequiredService<LaunchesDbContext>(),
            publisher,
            provider.GetRequiredService<IntegrationEventRegistry>(),
            clock,
            provider.GetRequiredService<IOptions<OutboxOptions>>(),
            provider.GetRequiredService<ILogger<OutboxDispatcher>>());

        await dispatcher.SweepAsync(CancellationToken.None);
    }

    private sealed class OffsetClock(TimeSpan offset) : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow.Add(offset);
    }

    private sealed class FailingPublisher : IIntegrationEventPublisher
    {
        public Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The broker is down.");
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<IntegrationEvent> Published { get; } = [];

        public Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }
}

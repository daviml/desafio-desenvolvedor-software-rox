using CashFlow.Launches.Application.Abstractions;
using CashFlow.Launches.Application.Entries;
using CashFlow.Launches.Application.Entries.CancelEntry;
using CashFlow.Launches.Application.Entries.GetEntryById;
using CashFlow.Launches.Application.Entries.ListEntries;
using CashFlow.Launches.Application.Entries.RegisterEntry;
using CashFlow.SharedKernel;
using CashFlow.SharedKernel.Application;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Launches.Application;

public static class LaunchesApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Composition root of the use cases. Registration is explicit rather than scanned:
    /// the full list of supported operations is visible in one place and cannot drift silently.
    /// </summary>
    public static IServiceCollection AddLaunchesApplication(this IServiceCollection services)
    {
        services.AddSharedKernel();

        services.AddRequestHandler<RegisterEntryCommand, EntryResponse, RegisterEntryCommandHandler>();
        services.AddRequestHandler<CancelEntryCommand, EntryResponse, CancelEntryCommandHandler>();
        services.AddRequestHandler<GetEntryByIdQuery, EntryResponse, GetEntryByIdQueryHandler>();
        services.AddRequestHandler<ListEntriesQuery, PagedResult<EntryResponse>, ListEntriesQueryHandler>();

        services.AddScoped<IValidator<RegisterEntryCommand>, RegisterEntryCommandValidator>();
        services.AddScoped<IValidator<CancelEntryCommand>, CancelEntryCommandValidator>();
        services.AddScoped<IValidator<ListEntriesQuery>, ListEntriesQueryValidator>();

        services.AddSingleton<IIntegrationEventFactory, LaunchesIntegrationEventFactory>();

        return services;
    }
}

using CashFlow.Consolidation.Application.Projection;
using CashFlow.Consolidation.Application.Reports;
using CashFlow.Consolidation.Application.Reports.GetDailyBalance;
using CashFlow.Consolidation.Application.Reports.GetStatement;
using CashFlow.Messaging;
using CashFlow.Messaging.Contracts;
using CashFlow.SharedKernel;
using CashFlow.SharedKernel.Application;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Consolidation.Application;

public static class ConsolidationApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddConsolidationApplication(this IServiceCollection services)
    {
        services.AddSharedKernel();

        services.AddRequestHandler<GetDailyBalanceQuery, DailyBalanceResponse, GetDailyBalanceQueryHandler>();
        services.AddRequestHandler<GetStatementQuery, StatementResponse, GetStatementQueryHandler>();

        services.AddScoped<IValidator<GetDailyBalanceQuery>, GetDailyBalanceQueryValidator>();
        services.AddScoped<IValidator<GetStatementQuery>, GetStatementQueryValidator>();

        services.AddScoped<DailyBalanceProjector>();
        services.AddScoped<IIntegrationEventHandler<EntryRegisteredIntegrationEvent>,
            EntryRegisteredIntegrationEventHandler>();
        services.AddScoped<IIntegrationEventHandler<EntryCancelledIntegrationEvent>,
            EntryCancelledIntegrationEventHandler>();

        return services;
    }
}

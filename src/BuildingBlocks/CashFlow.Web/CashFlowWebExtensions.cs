using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CashFlow.Web;

public static class CashFlowWebExtensions
{
    /// <summary>
    /// Registers the HTTP concerns both services share: JSON conventions, ProblemDetails,
    /// the global exception handler and response compression.
    /// </summary>
    public static IServiceCollection AddCashFlowWeb(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            // Enums travel as names, not ordinals: a contract that survives reordering.
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }

    /// <summary>Inserts the shared pipeline steps. Order matters: correlation first, then error handling.</summary>
    public static WebApplication UseCashFlowWeb(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        return app;
    }

    /// <summary>
    /// Publishes the two probes an orchestrator needs: liveness (is the process healthy?) and
    /// readiness (can it serve traffic?). Broker checks are excluded from liveness on purpose -
    /// a RabbitMQ outage must not restart a perfectly healthy API.
    /// </summary>
    public static WebApplication MapCashFlowHealthChecks(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
        }).WithTags("Health").ExcludeFromDescription();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }).WithTags("Health").ExcludeFromDescription();

        app.MapHealthChecks("/health").WithTags("Health").ExcludeFromDescription();

        return app;
    }
}

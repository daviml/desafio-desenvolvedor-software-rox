using System.Threading.RateLimiting;
using CashFlow.Consolidation.Api.Endpoints;
using CashFlow.Consolidation.Application;
using CashFlow.Consolidation.Infrastructure;
using CashFlow.Web;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
}
else
{
    builder.Logging.AddJsonConsole();
}

builder.Services.AddCashFlowWeb();
builder.Services.AddConsolidationApplication();
builder.Services.AddConsolidationInfrastructure(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new OpenApiInfo
{
    Title = "CashFlow - Consolidation API",
    Version = "v1",
    Description =
        "Consolidated daily balance per merchant. The projection is maintained asynchronously from " +
        "the entry events, so reporting load never touches the write path.",
}));

// Reads are the hot path here (the stated peak is 50 requests per second). A short output cache
// collapses bursts of identical report requests into a single database read; the window is small
// enough that a freshly consolidated entry shows up almost immediately.
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy(DailyBalanceEndpoints.ReportCachePolicy, policy => policy
        .Expire(TimeSpan.FromSeconds(5))
        .SetVaryByRouteValue("merchantId", "date")
        .SetVaryByQuery("from", "to"));
});

builder.Services.AddResponseCompression();

// Graceful degradation instead of collapse: the stated tolerance is a 5% loss at peak, and a
// bounded queue with fast rejection is what keeps the other 95% inside their latency budget.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "1";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { code = "rate_limit.exceeded", message = "Too many requests. Please retry shortly." },
            cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetTokenBucketLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 400,
                TokensPerPeriod = 200,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 200,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));
});

var app = builder.Build();

app.UseCashFlowWeb();
app.UseResponseCompression();
app.UseRateLimiter();
app.UseOutputCache();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Consolidation API v1"));
}

app.MapCashFlowHealthChecks();
app.MapDailyBalanceEndpoints();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Exposed so the integration tests can boot the real pipeline with WebApplicationFactory.</summary>
public partial class Program;

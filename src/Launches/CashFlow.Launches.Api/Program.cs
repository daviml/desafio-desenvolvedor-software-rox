using System.Threading.RateLimiting;
using CashFlow.Launches.Api.Endpoints;
using CashFlow.Launches.Application;
using CashFlow.Launches.Infrastructure;
using CashFlow.Web;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs outside development so a log pipeline can index the fields we emit
// (correlation id, request name, elapsed milliseconds) instead of parsing free text.
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
builder.Services.AddLaunchesApplication();
builder.Services.AddLaunchesInfrastructure(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new OpenApiInfo
{
    Title = "CashFlow - Launches API",
    Version = "v1",
    Description =
        "Registration of the merchant's cash flow entries (credits and debits). " +
        "Writes are durable and independent of the consolidation service: every entry is stored " +
        "together with its integration event in a transactional outbox.",
}));

// Load shedding. Under a traffic spike it is better to reject a slice of requests fast, with a
// Retry-After, than to let every request queue until the whole service times out.
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
                TokenLimit = 600,
                TokensPerPeriod = 300,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 100,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));
});

var app = builder.Build();

app.UseCashFlowWeb();
app.UseRateLimiter();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Launches API v1"));
}

app.MapCashFlowHealthChecks();
app.MapEntryEndpoints();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Exposed so the integration tests can boot the real pipeline with WebApplicationFactory.</summary>
public partial class Program;

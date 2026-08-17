using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using CashFlow.LoadTest;

// Open-loop load generator: requests are issued on a fixed schedule regardless of how fast the
// server answers. That is the honest way to measure a "requests per second" requirement - a
// closed-loop generator would silently slow down when the server does, hiding the very
// degradation the test is meant to expose.

var options = LoadTestOptions.Parse(args);

Console.WriteLine($"Target      : {options.Urls.Length} distinct URL(s), e.g. {options.Urls[0]}");
Console.WriteLine($"Rate        : {options.TargetRps} req/s");
Console.WriteLine($"Duration    : {options.DurationSeconds}s");
Console.WriteLine($"Warm-up     : {options.WarmupSeconds}s");
Console.WriteLine();

var handler = new SocketsHttpHandler
{
    // A single pool wide enough that the client is never the bottleneck.
    MaxConnectionsPerServer = Math.Max(64, options.TargetRps),
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
};

using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

await WarmUpAsync(client, options);

var results = new ConcurrentBag<RequestOutcome>();
var inFlight = new SemaphoreSlim(options.MaxInFlight);
var pending = new ConcurrentBag<Task>();
var interval = TimeSpan.FromSeconds(1.0 / options.TargetRps);
var run = Stopwatch.StartNew();
var scheduled = 0L;

while (run.Elapsed < TimeSpan.FromSeconds(options.DurationSeconds))
{
    var dueAt = TimeSpan.FromTicks(interval.Ticks * scheduled);
    var wait = dueAt - run.Elapsed;

    if (wait > TimeSpan.Zero)
    {
        await Task.Delay(wait);
    }

    // Round-robin over the URL set: with many distinct keys the output cache stops absorbing
    // everything, so the measurement reflects the database path rather than the cache.
    var url = options.Urls[(int)(scheduled % options.Urls.Length)];
    scheduled++;
    await inFlight.WaitAsync();

    pending.Add(Task.Run(async () =>
    {
        try
        {
            results.Add(await SendAsync(client, url));
        }
        finally
        {
            inFlight.Release();
        }
    }));
}

await Task.WhenAll(pending);
run.Stop();

Report.Print(options, [.. results], run.Elapsed);

static async Task WarmUpAsync(HttpClient client, LoadTestOptions options)
{
    if (options.WarmupSeconds <= 0)
    {
        return;
    }

    Console.WriteLine("Warming up (JIT, connection pool, EF query cache)...");
    var warmup = Stopwatch.StartNew();
    var index = 0;

    while (warmup.Elapsed < TimeSpan.FromSeconds(options.WarmupSeconds))
    {
        await SendAsync(client, options.Urls[index++ % options.Urls.Length]);
    }

    Console.WriteLine("Warm-up done.");
    Console.WriteLine();
}

static async Task<RequestOutcome> SendAsync(HttpClient client, Uri url)
{
    var timestamp = Stopwatch.GetTimestamp();

    try
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead);
        var elapsed = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

        return new RequestOutcome(response.StatusCode, elapsed, Faulted: false);
    }
    catch (Exception)
    {
        // Timeouts and connection failures count as lost requests, not as fast responses.
        return new RequestOutcome(null, Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds, Faulted: true);
    }
}

namespace CashFlow.LoadTest
{
    internal readonly record struct RequestOutcome(HttpStatusCode? StatusCode, double ElapsedMs, bool Faulted)
    {
        public bool IsSuccess => !Faulted && StatusCode is HttpStatusCode.OK;

        public bool IsShedLoad => StatusCode is HttpStatusCode.TooManyRequests;
    }

    internal sealed record LoadTestOptions(Uri[] Urls, int TargetRps, int DurationSeconds, int WarmupSeconds)
    {
        /// <summary>Bounds client-side concurrency so the generator itself cannot exhaust sockets.</summary>
        public int MaxInFlight => Math.Max(64, TargetRps * 4);

        public static LoadTestOptions Parse(string[] args)
        {
            var url = Get(args, "--url");
            var urlsFile = Get(args, "--urls-file");

            Uri[] urls = urlsFile is not null
                ? [.. File.ReadAllLines(urlsFile)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
                    .Select(line => new Uri(line))]
                : url is not null
                    ? [new Uri(url)]
                    : throw new ArgumentException(
                        "Usage: dotnet run -- (--url <url> | --urls-file <path>) [--rps 100] [--duration 60] [--warmup 5]");

            if (urls.Length == 0)
            {
                throw new ArgumentException("The URL file is empty.");
            }

            return new LoadTestOptions(
                urls,
                int.Parse(Get(args, "--rps") ?? "100", CultureInfo.InvariantCulture),
                int.Parse(Get(args, "--duration") ?? "60", CultureInfo.InvariantCulture),
                int.Parse(Get(args, "--warmup") ?? "5", CultureInfo.InvariantCulture));
        }

        private static string? Get(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }

    internal static class Report
    {
        public static void Print(LoadTestOptions options, RequestOutcome[] outcomes, TimeSpan elapsed)
        {
            if (outcomes.Length == 0)
            {
                Console.WriteLine("No requests were completed.");
                return;
            }

            var successes = outcomes.Where(outcome => outcome.IsSuccess).ToArray();
            var shed = outcomes.Count(outcome => outcome.IsShedLoad);
            var faulted = outcomes.Count(outcome => outcome.Faulted);
            var otherErrors = outcomes.Length - successes.Length - shed - faulted;

            var latencies = successes.Select(outcome => outcome.ElapsedMs).Order().ToArray();

            Console.WriteLine("─────────────────────────────────────────────");
            Console.WriteLine($"Requests issued    : {outcomes.Length}");
            Console.WriteLine($"Achieved rate      : {outcomes.Length / elapsed.TotalSeconds:0.0} req/s");
            Console.WriteLine($"Successful (200)   : {successes.Length} ({Percent(successes.Length, outcomes.Length):0.00}%)");
            Console.WriteLine($"Shed (429)         : {shed} ({Percent(shed, outcomes.Length):0.00}%)");
            Console.WriteLine($"Other status codes : {otherErrors}");
            Console.WriteLine($"Faulted / timeout  : {faulted}");
            Console.WriteLine();

            if (latencies.Length > 0)
            {
                Console.WriteLine("Latency of successful responses (ms)");
                Console.WriteLine($"  min  : {latencies[0]:0.00}");
                Console.WriteLine($"  p50  : {Percentile(latencies, 50):0.00}");
                Console.WriteLine($"  p95  : {Percentile(latencies, 95):0.00}");
                Console.WriteLine($"  p99  : {Percentile(latencies, 99):0.00}");
                Console.WriteLine($"  max  : {latencies[^1]:0.00}");
                Console.WriteLine($"  mean : {latencies.Average():0.00}");
            }

            Console.WriteLine("─────────────────────────────────────────────");

            var lossPercent = Percent(outcomes.Length - successes.Length, outcomes.Length);
            var verdict = lossPercent <= 5.0 ? "PASS" : "FAIL";
            Console.WriteLine($"Loss: {lossPercent:0.00}% (budget 5.00%) -> {verdict}");
        }

        private static double Percent(int part, int total) => total == 0 ? 0 : part * 100.0 / total;

        /// <summary>Nearest-rank percentile over an already sorted sample.</summary>
        private static double Percentile(double[] sorted, int percentile)
        {
            var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Length);
            return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
        }
    }
}

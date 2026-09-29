using System.Net;
using Mapna.Sender.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Serilog;

namespace Mapna.Sender;

/// <summary>
/// HTTP resilience for the receiver, on Polly v8 (Microsoft.Extensions.Http.Resilience). Microsoft.Extensions.Http.Polly
/// is deprecated. Changes from the old policy, each tied to a finding in the review:
///  * Retry honours Retry-After on 429/503 (the old fixed 2/4/8s backoff hammered a limiter that asked for 60s).
///  * Per-attempt timeout plus a total timeout; HttpClient.Timeout is infinite so it can't cut the retries off halfway.
///  * The breaker opens only on CONNECTIVITY failures (network, timeout, 502/503/504). A deterministic 500 for one
///    bad record used to count toward it, so two adjacent poison records tripped it, and the orchestrator waited
///    and retried the same poison record forever.
/// </summary>
public static class ReceiverHttpPipeline
{
    public const string ClientName = "ReceiverApi";
    public static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30);

    public static ServiceProvider Build(AppSettings settings, ILogger logger, Guid runId, Func<HttpMessageHandler>? primaryHandler = null)
    {
        var services = new ServiceCollection();
        var httpBuilder = services.AddHttpClient(ClientName, client =>
            {
                client.BaseAddress = settings.ReceiverBaseUri;
                client.Timeout = Timeout.InfiniteTimeSpan;
                if (!string.IsNullOrWhiteSpace(settings.ReceiverApiKey))
                    client.DefaultRequestHeaders.Add("X-Api-Key", settings.ReceiverApiKey);
            });

        if (primaryHandler is not null)
            httpBuilder.ConfigurePrimaryHttpMessageHandler(primaryHandler);

        httpBuilder.AddResilienceHandler("receiver", builder =>
            {
                builder.AddTimeout(TimeSpan.FromMinutes(3));

                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(2),
                    MaxDelay = TimeSpan.FromSeconds(60),
                    ShouldRetryAfterHeader = true,
                    // Default ShouldHandle: HttpRequestException, TimeoutRejectedException, 5xx, 408, 429.
                });

                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.9,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = BreakDuration,
                    ShouldHandle = args => ValueTask.FromResult(IsConnectivityFailure(args.Outcome)),
                    OnOpened = args =>
                    {
                        logger.ForContext("EventType", LoggingSetup.CircuitOpened).ForContext("RunId", runId)
                            .Warning("HTTP circuit opened for {BreakSeconds:0}s (Receiver API unreachable)", args.BreakDuration.TotalSeconds);
                        return ValueTask.CompletedTask;
                    },
                    OnClosed = _ =>
                    {
                        logger.ForContext("EventType", LoggingSetup.CircuitClosed).ForContext("RunId", runId)
                            .Information("HTTP circuit closed - Receiver API is reachable again");
                        return ValueTask.CompletedTask;
                    }
                });

                builder.AddTimeout(TimeSpan.FromSeconds(30));
            });

        return services.BuildServiceProvider();
    }

    private static bool IsConnectivityFailure(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is HttpRequestException or TimeoutRejectedException ||
        outcome.Result?.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}

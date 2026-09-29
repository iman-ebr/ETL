using System.Net;
using Mapna.LogData;
using Mapna.Sender;
using Newtonsoft.Json;
using Polly;
using Polly.CircuitBreaker;

namespace Mapna.Tests;

public class SendDecisionTests
{
    [Fact]
    public void Never_sent_record_is_sent()
    {
        var service = new SendDecisionService(new Dictionary<int, SendState>());
        Assert.Equal(SendAction.Send, service.Decide(TestData.Record(1)).Action);
    }

    [Fact]
    public void Unchanged_record_is_skipped_and_changed_fields_are_reported()
    {
        var sent = TestData.Record(1);
        var states = new Dictionary<int, SendState>
        {
            [1] = new() { PerId = 1, PayloadSnapshot = JsonConvert.SerializeObject(sent), LastStatus = SendStatus.Sent }
        };
        var service = new SendDecisionService(states);

        Assert.Equal(SendAction.SkipDuplicate, service.Decide(TestData.Record(1)).Action);

        var changed = service.Decide(TestData.Record(1, r => { r.PerSurname = "کریمی"; r.MobileNo = "09120000000"; }));
        Assert.Equal(SendAction.Send, changed.Action);
        Assert.Equal("PerSurname,MobileNo", changed.ChangedFields);
    }

    [Fact]
    public void State_row_without_confirmed_snapshot_means_send()
    {
        var states = new Dictionary<int, SendState> { [1] = new() { PerId = 1, PayloadSnapshot = null, LastStatus = SendStatus.SendFailed } };
        Assert.Equal(SendAction.Send, new SendDecisionService(states).Decide(TestData.Record(1)).Action);
    }

    [Fact]
    public void Duplicate_perIds_in_source_are_quarantined_not_picked_arbitrarily()
    {
        var snapshot = SourceSnapshot.Create([TestData.Record(1), TestData.Record(2), TestData.Record(2, r => r.PerName = "دیگر")]);
        Assert.Single(snapshot.ByPerId);
        Assert.Equal(2, snapshot.DuplicatePerIds[2]);
        Assert.Equal(2, snapshot.Count);
    }
}

public class RecordSenderClassificationTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return respond(request, cancellationToken);
        }
    }

    private static (RecordSender Sender, StubHandler Handler) Create(HttpStatusCode code, string body = "{}")
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) }));
        return (new RecordSender(new HttpClient(handler) { BaseAddress = new Uri("http://receiver.test/") }), handler);
    }

    private static SendDecision SendIt() => new() { Action = SendAction.Send, PayloadSnapshot = "{}" };

    [Fact]
    public async Task Success_is_confirmed_and_carries_a_correlation_id_header()
    {
        var (sender, handler) = Create(HttpStatusCode.OK, """{"perId":1,"status":"Updated"}""");
        var outcome = await sender.SendAsync(TestData.Record(1), SendIt(), CancellationToken.None);

        Assert.Equal(SendStatus.Sent, outcome.Status);
        Assert.True(outcome.ConfirmedByReceiver);
        Assert.Equal("Receiver: Updated", outcome.Reason);
        var header = Assert.Single(handler.Requests).Headers.GetValues(RecordSender.CorrelationHeader).Single();
        Assert.Equal(outcome.CorrelationId.ToString(), header);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Permanent_rejections_fail_only_the_record(HttpStatusCode code)
    {
        var (sender, _) = Create(code);
        var outcome = await sender.SendAsync(TestData.Record(1), SendIt(), CancellationToken.None);
        Assert.Equal(SendStatus.SendFailed, outcome.Status);
        Assert.False(outcome.ConfirmedByReceiver);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Configuration_errors_stop_the_run_instead_of_failing_every_record(HttpStatusCode code)
    {
        var (sender, _) = Create(code);
        await Assert.ThrowsAsync<ReceiverConfigurationException>(() => sender.SendAsync(TestData.Record(1), SendIt(), CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Transient_answers_keep_the_record_pending(HttpStatusCode code)
    {
        var (sender, _) = Create(code);
        await Assert.ThrowsAsync<TransientSendException>(() => sender.SendAsync(TestData.Record(1), SendIt(), CancellationToken.None));
    }

    [Fact]
    public async Task A_timeout_is_transient_not_a_user_cancellation()
    {
        // The old code rethrew every OperationCanceledException, so ONE slow request aborted the whole run and
        // labelled it "cancelled by user".
        var handler = new StubHandler((_, _) => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
        var sender = new RecordSender(new HttpClient(handler) { BaseAddress = new Uri("http://receiver.test/") });
        await Assert.ThrowsAsync<TransientSendException>(() => sender.SendAsync(TestData.Record(1), SendIt(), CancellationToken.None));
    }

    [Fact]
    public async Task A_real_user_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHandler(async (_, ct) => { await cts.CancelAsync(); ct.ThrowIfCancellationRequested(); return new HttpResponseMessage(HttpStatusCode.OK); });
        var sender = new RecordSender(new HttpClient(handler) { BaseAddress = new Uri("http://receiver.test/") });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendAsync(TestData.Record(1), SendIt(), cts.Token));
    }

    [Fact]
    public async Task Skips_never_touch_the_network()
    {
        var (sender, handler) = Create(HttpStatusCode.OK);
        await sender.SendAsync(TestData.Record(1), new SendDecision { Action = SendAction.SkipDuplicate, PayloadSnapshot = "{}" }, CancellationToken.None);
        await sender.SendAsync(TestData.Record(1), new SendDecision { Action = SendAction.SkipValidationFailed, PayloadSnapshot = "{}" }, CancellationToken.None);
        Assert.Empty(handler.Requests);
    }
}

public class ResilienceTypeTests
{
    /// <summary>
    /// The orchestrator catches one BrokenCircuitException type for both the Polly v7-style SQL policies and the
    /// v8 HTTP pipeline. If they were different types, one catch would silently miss and a connectivity blip
    /// would abort the run instead of pausing it. This proves they are the same runtime type.
    /// </summary>
    [Fact]
    public async Task V7_policies_and_v8_pipelines_throw_the_same_BrokenCircuitException()
    {
        var v7 = Policy.Handle<InvalidOperationException>().CircuitBreakerAsync(1, TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => v7.ExecuteAsync(() => throw new InvalidOperationException()));
        var v7Ex = await Assert.ThrowsAnyAsync<Exception>(() => v7.ExecuteAsync(() => Task.CompletedTask));

        var v8 = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5, MinimumThroughput = 2, BreakDuration = TimeSpan.FromMinutes(1),
                ShouldHandle = new PredicateBuilder().Handle<InvalidOperationException>()
            })
            .Build();
        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAsync<InvalidOperationException>(() => v8.ExecuteAsync(_ => throw new InvalidOperationException()).AsTask());
        var v8Ex = await Assert.ThrowsAnyAsync<Exception>(() => v8.ExecuteAsync(_ => ValueTask.CompletedTask).AsTask());

        Assert.IsAssignableFrom<BrokenCircuitException>(v7Ex);
        Assert.IsAssignableFrom<BrokenCircuitException>(v8Ex);
    }

    [Fact]
    public void Deterministic_sql_errors_are_not_transient()
    {
        Assert.True(SqlTransientErrors.IsTransient(new TimeoutException()));
        Assert.False(SqlTransientErrors.IsTransient(new InvalidOperationException()));
    }
}

public class AppSettingsTests
{
    [Fact]
    public void Placeholder_api_key_and_bad_url_are_rejected_before_a_run_is_created()
    {
        var errors = new AppSettings { SourceConnectionString = "x", AppConnectionString = "x", ReceiverApiBaseUrl = "not a url", ReceiverApiKey = "CHANGE-ME-local-dev-key" }.Validate();
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void Base_url_without_trailing_slash_keeps_its_path()
    {
        var settings = new AppSettings { ReceiverApiBaseUrl = "https://host/receiver" };
        Assert.Equal("https://host/receiver/api/personnel", new Uri(settings.ReceiverBaseUri, "api/personnel").ToString());
    }
}

namespace Mapna.Sender;

/// <summary>
/// Cooperative pause. The orchestrator only checks it BETWEEN records, so pausing never interrupts an in-flight
/// HTTP request and never leaves a record's outcome ambiguous.
/// </summary>
public sealed class PauseTokenSource
{
    private TaskCompletionSource? _resumeSignal;

    public bool IsPaused => Volatile.Read(ref _resumeSignal) is not null;

    public PauseToken Token => new(this);

    public void Pause() =>
        Interlocked.CompareExchange(ref _resumeSignal, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), null);

    public void Resume() => Interlocked.Exchange(ref _resumeSignal, null)?.TrySetResult();

    internal Task WaitForResumeAsync(CancellationToken cancellationToken) =>
        Volatile.Read(ref _resumeSignal)?.Task.WaitAsync(cancellationToken) ?? Task.CompletedTask;
}

public readonly struct PauseToken
{
    private readonly PauseTokenSource? _source;

    internal PauseToken(PauseTokenSource source) => _source = source;

    public bool IsPaused => _source?.IsPaused ?? false;

    public Task WaitForResumeAsync(CancellationToken cancellationToken) =>
        _source?.WaitForResumeAsync(cancellationToken) ?? Task.CompletedTask;
}

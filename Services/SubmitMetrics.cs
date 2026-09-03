using System.Diagnostics;

namespace ApproveFlow.MVC.Services;

// ── Structured stage timing for workflow submit actions ──────────────────────
// Answers "which stage is slow?" without a profiler. One log line per submit,
// listing every stage and its elapsed milliseconds, e.g.
//
//   SUBMITPERF Submit RequestId=469 User=12 Total=142ms | load=31ms
//   validate=4ms approvers=63ms notify-queued=1ms save=43ms
//
// Any stage over SlowStageMs is additionally logged at Warning so slow stages
// surface in the log without trawling. Purely observational — it never changes
// control flow, and Dispose is safe to call more than once.
public sealed class SubmitTimer : IDisposable
{
    private const int SlowStageMs = 500;

    private readonly ILogger _log;
    private readonly string _operation;
    private readonly int _requestId;
    private readonly int _userId;
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly List<(string Stage, long Ms)> _stages = new();
    private long _lastMark;
    private bool _done;

    public SubmitTimer(ILogger log, string operation, int requestId, int userId)
    { _log = log; _operation = operation; _requestId = requestId; _userId = userId; }

    // Records the time since the previous Mark (or since construction for the first).
    public void Mark(string stage)
    {
        var now = _total.ElapsedMilliseconds;
        var ms = now - _lastMark;
        _lastMark = now;
        _stages.Add((stage, ms));
        if (ms >= SlowStageMs)
            _log.LogWarning("SUBMITPERF SLOW STAGE {Operation} RequestId={RequestId} Stage={Stage} Ms={Ms}",
                _operation, _requestId, stage, ms);
    }

    public void Dispose()
    {
        if (_done) return;
        _done = true;
        _total.Stop();
        var breakdown = string.Join(" ", _stages.Select(s => $"{s.Stage}={s.Ms}ms"));
        _log.LogInformation("SUBMITPERF {Operation} RequestId={RequestId} User={UserId} Total={TotalMs}ms | {Breakdown}",
            _operation, _requestId, _userId, _total.ElapsedMilliseconds, breakdown);
    }
}

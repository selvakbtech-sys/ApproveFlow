using System.Threading.Channels;

namespace ApproveFlow.MVC.Services;

// ── Fire-and-forget job queue ────────────────────────────────────────────────
// Work that must NOT hold up the user's HTTP request is handed here instead of
// being awaited inline. The submit path's WhatsApp notification is the motivating
// case: a TLS reset to graph.facebook.com made SendNewRequestToL1Async take 30-90s
// (3 media-upload attempts + 2 template attempts, each with a 15s ConnectTimeout),
// and the user sat on a dead page clicking Submit again.
//
// This is a scheduling change only — the queued delegate runs exactly the same
// service methods it used to, including the WhatsAppOutbox fallback for transport
// failures. Nothing about message content or ordering per request changes.
public interface IBackgroundJobQueue
{
    // Queues a job. Never throws and never blocks: the channel is unbounded, so an
    // enqueue during a Meta outage still returns immediately.
    void Enqueue(string name, Func<IServiceProvider, CancellationToken, Task> job);
}

public sealed class BackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<(string Name, Func<IServiceProvider, CancellationToken, Task> Job)> _ch =
        Channel.CreateUnbounded<(string, Func<IServiceProvider, CancellationToken, Task>)>(
            new UnboundedChannelOptions { SingleReader = true });

    private readonly ILogger<BackgroundJobQueue> _log;
    public BackgroundJobQueue(ILogger<BackgroundJobQueue> log) => _log = log;

    public void Enqueue(string name, Func<IServiceProvider, CancellationToken, Task> job)
    {
        if (!_ch.Writer.TryWrite((name, job)))
            _log.LogError("BackgroundJobQueue: could not enqueue {Job} — channel closed.", name);
    }

    internal IAsyncEnumerable<(string Name, Func<IServiceProvider, CancellationToken, Task> Job)> ReadAllAsync(
        CancellationToken ct) => _ch.Reader.ReadAllAsync(ct);
}

// Single-reader drain loop. Each job gets its OWN DI scope (and therefore its own
// AppDbContext) because the scope that queued it — the HTTP request — is long gone
// by the time the job runs.
public sealed class BackgroundJobService : BackgroundService
{
    private readonly BackgroundJobQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BackgroundJobService> _log;

    public BackgroundJobService(IBackgroundJobQueue queue, IServiceScopeFactory scopes, ILogger<BackgroundJobService> log)
    { _queue = (BackgroundJobQueue)queue; _scopes = scopes; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (name, job) in _queue.ReadAllAsync(stoppingToken))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var scope = _scopes.CreateScope();
                await job(scope.ServiceProvider, stoppingToken);
                _log.LogInformation("BGJOB {Job} completed in {Ms}ms.", name, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // App shutting down — the WhatsAppOutbox picks up anything undelivered.
                break;
            }
            catch (Exception ex)
            {
                // A background job must never take the host down.
                _log.LogError(ex, "BGJOB {Job} failed after {Ms}ms.", name, sw.ElapsedMilliseconds);
            }
        }
    }
}

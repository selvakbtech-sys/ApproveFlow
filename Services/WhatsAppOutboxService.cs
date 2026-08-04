namespace ApproveFlow.MVC.Services;

/// <summary>
/// Resends WhatsApp messages that failed at the TRANSPORT layer (e.g. an intermittent TLS reset
/// to graph.facebook.com — SocketException 10054). Those sends are queued into WhatsAppOutbox by
/// <see cref="WhatsAppService.PostToWhatsAppAsync"/> once the quick inline retry is exhausted, so
/// the user's action returns immediately. This timer then re-POSTs each due row on its own
/// backoff until it delivers or is abandoned, riding out outages that last minutes.
///
/// Runs every WhatsApp:OutboxPollSeconds (default 60). Each row is retried up to
/// WhatsApp:OutboxMaxAttempts (default 8) times with exponential backoff before being abandoned.
/// Only connection-phase failures are ever queued — a real API rejection is never resent.
/// </summary>
public class WhatsAppOutboxService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<WhatsAppOutboxService> _log;

    public WhatsAppOutboxService(
        IServiceScopeFactory scopeFactory,
        IConfiguration cfg,
        ILogger<WhatsAppOutboxService> log)
    {
        _scopeFactory = scopeFactory;
        _cfg = cfg;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSec = _cfg.GetValue<int>("WhatsApp:OutboxPollSeconds", 60);
        if (intervalSec < 5) intervalSec = 60;
        _log.LogInformation("WhatsAppOutboxService started (every {Sec}s).", intervalSec);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // A fresh scope per tick — the DbContext and WhatsAppService are scoped, and
                // this is the single processor, so ticks never overlap (awaited sequentially).
                using var scope = _scopeFactory.CreateScope();
                var wa = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
                await wa.ProcessOutboxOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "WhatsApp outbox processing failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSec), stoppingToken);
            }
            catch (TaskCanceledException) { break; }
        }
    }
}

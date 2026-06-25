namespace ApproveFlow.MVC.Services;

/// <summary>
/// Hosted background service that fires the WhatsApp aging reminder once a day.
/// Default schedule: every day at 09:00 IST.
/// Configure via appsettings: WhatsApp:ReminderHour (0-23, IST, default 9).
/// </summary>
public class AgingReminderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<AgingReminderBackgroundService> _log;

    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    public AgingReminderBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration cfg,
        ILogger<AgingReminderBackgroundService> log)
    {
        _scopeFactory = scopeFactory;
        _cfg  = cfg;
        _log  = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("AgingReminderBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeNextDelay();
            _log.LogInformation("Next WhatsApp aging reminder in {Minutes} minutes.", (int)delay.TotalMinutes);

            await Task.Delay(delay, stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;

            await RunAsync(stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
            _log.LogInformation("Sending WhatsApp aging reminder messages...");
            await svc.SendAgingReminderAsync();
            _log.LogInformation("WhatsApp aging reminder completed.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Aging reminder failed.");
        }
    }

    /// <summary>
    /// Returns time-span until the next scheduled reminder hour in IST.
    /// </summary>
    private TimeSpan ComputeNextDelay()
    {
        int targetHour = _cfg.GetValue<int>("WhatsApp:ReminderHour", 9);

        var nowIst  = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
        var todayRun = new DateTimeOffset(
            nowIst.Year, nowIst.Month, nowIst.Day,
            targetHour, 0, 0, nowIst.Offset);

        // If we already passed today's run time, schedule for tomorrow
        if (nowIst >= todayRun)
            todayRun = todayRun.AddDays(1);

        return todayRun - nowIst;
    }
}

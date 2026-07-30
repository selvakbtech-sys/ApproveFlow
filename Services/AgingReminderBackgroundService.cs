namespace ApproveFlow.MVC.Services;

/// <summary>
/// Hosted background service that fires the WhatsApp pending-approval reminder at one or more
/// fixed times every day (IST). Each firing reuses <see cref="IWhatsAppService.SendAgingReminderAsync"/>
/// unchanged, so the reminder still uses the shared pending-approval query and the
/// pending_approval_summary template, sends one consolidated message per approver, and skips
/// approvers with nothing pending.
///
/// Schedule is configuration-driven so it can change without code:
///   WhatsApp:PendingReminderTimes = [ "08:59", "13:00", "18:00" ]  (IST, 24h "HH:mm").
/// If that list is absent or empty it falls back to a single daily run at
/// WhatsApp:ReminderHour (0-23, default 9) — the original behaviour.
///
/// Duplicate protection ("never send twice for the same slot"):
///   1. an in-memory marker of the last fired slot, which absorbs a timer that wakes a hair
///      early and would otherwise re-select the same slot, and
///   2. a durable check against WhatsAppMessageHistory: if a reminder was already logged inside
///      this slot's window today (e.g. after an app-pool recycle) the slot is skipped.
/// </summary>
public class AgingReminderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<AgingReminderBackgroundService> _log;

    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    // The slot most recently fired by THIS process, as "yyyy-MM-dd HH:mm". Guards against a
    // Task.Delay that returns a few milliseconds early re-selecting the very same slot.
    private string? _lastFiredSlotKey;

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
        _log.LogInformation("AgingReminderBackgroundService started. Schedule (IST): {Times}",
            string.Join(", ", GetScheduleTimes().Select(t => t.ToString("HH\\:mm"))));

        while (!stoppingToken.IsCancellationRequested)
        {
            var nowIst = IstClock.Now;
            var slot   = NextSlotAfter(nowIst);

            _log.LogInformation("Next WhatsApp pending reminder at {Slot:yyyy-MM-dd HH:mm} IST (in {Minutes} min).",
                slot, (int)(slot - nowIst).TotalMinutes);

            try
            {
                await Task.Delay(slot - IstClock.Now, stoppingToken);
            }
            catch (TaskCanceledException) { break; }

            if (stoppingToken.IsCancellationRequested) break;

            await FireSlotAsync(slot, stoppingToken);
        }
    }

    // Runs one scheduled slot, unless duplicate protection says it has already gone out.
    private async Task FireSlotAsync(DateTimeOffset slot, CancellationToken ct)
    {
        var slotKey = slot.ToString("yyyy-MM-dd HH:mm");

        // (1) In-memory guard: same process, same slot already handled.
        if (_lastFiredSlotKey == slotKey)
            return;

        // (2) Durable guard: a reminder already logged inside this slot's window today. The
        // window runs from this slot until the next scheduled slot, so a batch that spans a
        // few seconds is captured while the neighbouring slots are never mistaken for it.
        var windowEnd = NextSlotAfter(slot);
        if (await AlreadySentForSlotAsync(slot, windowEnd, ct))
        {
            _lastFiredSlotKey = slotKey;
            _log.LogInformation("WA-REMINDER: slot {Slot} already sent today — skipping (duplicate protection).", slotKey);
            return;
        }

        _lastFiredSlotKey = slotKey;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
            _log.LogInformation("Sending WhatsApp pending reminder for slot {Slot}...", slotKey);
            await svc.SendAgingReminderAsync();
            _log.LogInformation("WhatsApp pending reminder completed for slot {Slot}.", slotKey);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Pending reminder failed for slot {Slot}.", slotKey);
        }
    }

    // True when WhatsAppMessageHistory already holds an outbound pending-reminder row inside
    // [slotStart, windowEnd) today. This is the only place these reminders are sent, so such a
    // row can only mean this slot has already fired — survives a restart between fire and now.
    private async Task<bool> AlreadySentForSlotAsync(DateTimeOffset slotStart, DateTimeOffset windowEnd, CancellationToken ct)
    {
        // The message types SendAgingReminderAsync writes for a reminder: the summary template,
        // the free-form fallback, and the final name+count template fallback.
        var markers = new List<string> { "pending_reminder", "template:pending_approval_reminder" };
        var template = _cfg["WhatsApp:PendingReminderTemplate"];
        if (!string.IsNullOrWhiteSpace(template))
            markers.Add($"template:{template}");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.WhatsAppMessageHistories.AsNoTracking()
                .AnyAsync(h => h.Direction == "Outbound"
                            && h.CreatedDate >= slotStart
                            && h.CreatedDate < windowEnd
                            && markers.Contains(h.MessageType), ct);
        }
        catch (Exception ex)
        {
            // If the guard query fails we prefer to send (a possible duplicate) over silently
            // dropping the reminder — but log it so the failure is visible.
            _log.LogWarning(ex, "WA-REMINDER: duplicate-protection check failed for slot {Slot:yyyy-MM-dd HH:mm}; proceeding.", slotStart);
            return false;
        }
    }

    // The earliest configured slot strictly after <paramref name="reference"/> (IST). When
    // every slot for that day has passed, the first slot of the next day.
    private DateTimeOffset NextSlotAfter(DateTimeOffset reference)
    {
        var times = GetScheduleTimes();
        var refIst = TimeZoneInfo.ConvertTime(reference, Ist);

        foreach (var t in times)
        {
            var candidate = new DateTimeOffset(
                refIst.Year, refIst.Month, refIst.Day, t.Hour, t.Minute, 0, refIst.Offset);
            if (candidate > refIst)
                return candidate;
        }

        var tomorrow = refIst.Date.AddDays(1);
        var first = times[0];
        return new DateTimeOffset(
            tomorrow.Year, tomorrow.Month, tomorrow.Day, first.Hour, first.Minute, 0, refIst.Offset);
    }

    // Parses WhatsApp:PendingReminderTimes ("HH:mm", IST) into a sorted, de-duplicated list.
    // Falls back to a single slot at WhatsApp:ReminderHour when the list is missing/invalid,
    // preserving the original once-a-day behaviour.
    private List<TimeOnly> GetScheduleTimes()
    {
        var raw = _cfg.GetSection("WhatsApp:PendingReminderTimes").Get<string[]>();
        var times = new List<TimeOnly>();

        if (raw != null)
        {
            foreach (var s in raw)
            {
                if (TimeOnly.TryParseExact(s?.Trim(), "HH:mm", out var t))
                    times.Add(t);
                else if (!string.IsNullOrWhiteSpace(s))
                    _log.LogWarning("WA-REMINDER: ignoring invalid PendingReminderTimes entry '{Entry}' (expected HH:mm).", s);
            }
        }

        if (times.Count == 0)
        {
            int hour = _cfg.GetValue<int>("WhatsApp:ReminderHour", 9);
            if (hour < 0 || hour > 23) hour = 9;
            times.Add(new TimeOnly(hour, 0));
        }

        return times.Distinct().OrderBy(t => t).ToList();
    }
}

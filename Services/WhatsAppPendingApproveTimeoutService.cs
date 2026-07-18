namespace ApproveFlow.MVC.Services;

/// <summary>
/// Sweeps for DEFERRED WhatsApp approvals whose optional-reply window has elapsed and
/// executes them automatically (no remark). A deferred approve is a
/// WhatsAppPendingClarification row with Action = "approved": it is created when the
/// approver taps Approve and left WaitingForReply until ExpiresOn, which is
/// WhatsApp:ApprovalReplyWindowSeconds (default 30) after the tap. Runs every
/// WhatsApp:ApproveTimeoutSweepSeconds (default 10).
///
/// Sessions are per REQUEST and fully independent: an approver may have any number open
/// at once, and each is swept and executed on its own without reference to the others.
///
/// Exactly-once is guaranteed by IRequestService.TryExecuteDeferredApproveAsync, which
/// atomically claims the session (WaitingForReply → Completed) before approving — so a
/// sweep and a simultaneous approver reply can never both execute the same approval.
/// </summary>
public class WhatsAppPendingApproveTimeoutService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<WhatsAppPendingApproveTimeoutService> _log;

    public WhatsAppPendingApproveTimeoutService(
        IServiceScopeFactory scopeFactory,
        IConfiguration cfg,
        ILogger<WhatsAppPendingApproveTimeoutService> log)
    {
        _scopeFactory = scopeFactory;
        _cfg = cfg;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Kill switch, ON by default — every environment sweeps unless explicitly told not
        // to. Turning it off stops deferred approvals auto-approving FROM THIS INSTANCE:
        // useful only when a second instance is pointed at the same database and must not
        // act on it (all instances share one database — see appsettings.json). Do not
        // disable it in an environment you actually test in; nothing will auto-approve.
        if (!_cfg.GetValue<bool>("WhatsApp:EnableApproveTimeoutSweep", true))
        {
            _log.LogWarning("WhatsAppPendingApproveTimeoutService DISABLED (WhatsApp:EnableApproveTimeoutSweep=false). " +
                            "Deferred approvals will NOT auto-approve from this instance.");
            return;
        }

        var intervalSec = _cfg.GetValue<int>("WhatsApp:ApproveTimeoutSweepSeconds", 10);
        if (intervalSec < 1) intervalSec = 10;
        _log.LogInformation("WhatsAppPendingApproveTimeoutService started (every {Sec}s).", intervalSec);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Deferred-approve timeout sweep failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSec), stoppingToken);
            }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var windowSec = _cfg.GetValue<int>("WhatsApp:ApprovalReplyWindowSeconds", 30);
        var now = IstClock.Now;
        var cutoff = now.AddSeconds(-windowSec);

        List<WhatsAppPendingClarification> due;
        using (var queryScope = _scopeFactory.CreateScope())
        {
            var db = queryScope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Every still-open session whose window has elapsed — there is no per-approver
            // limit, so any number may come due together and each is executed independently.
            // Sessions created before ExpiresOn existed fall back to CreatedAt + window.
            due = await db.WhatsAppPendingClarifications
                .AsNoTracking()
                .Where(p => p.Action == "approved" && p.Status == "WaitingForReply"
                         && ((p.ExpiresOn != null && p.ExpiresOn <= now)
                          || (p.ExpiresOn == null && p.CreatedAt <= cutoff)))
                .OrderBy(p => p.CreatedAt)
                .ToListAsync(ct);
        }

        if (due.Count == 0) return;

        foreach (var p in due)
        {
            if (ct.IsCancellationRequested) break;

            // A fresh scope PER SESSION. Approvers may now hold many sessions at once, so
            // a whole batch routinely comes due together: giving each its own DbContext
            // keeps one request's tracked state — or one failure — from affecting the next.
            try
            {
                using var itemScope = _scopeFactory.CreateScope();
                var db = itemScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var svc = itemScope.ServiceProvider.GetRequiredService<IRequestService>();
                var wa = itemScope.ServiceProvider.GetRequiredService<IWhatsAppService>();

                var (claimed, ok, err) = await svc.TryExecuteDeferredApproveAsync(
                    p.Id, p.RequestId, p.ApproverId, null);
                if (!claimed) continue; // an approver reply already claimed and executed it

                if (ok)
                {
                    var reqInfo = await db.Requests.AsNoTracking()
                        .Where(r => r.RequestId == p.RequestId)
                        .Select(r => new { r.RequestNumber, r.Title })
                        .FirstOrDefaultAsync(ct);
                    // Finalises the original interaction: the approver is told the window
                    // closed and the request is done, so the now-inactive buttons on the
                    // original card are never expected to do anything further.
                    await wa.SendPlainTextAsync(p.Phone,
                        $"✅ Request Approved\n\n" +
                        $"(*{reqInfo?.RequestNumber}* - {reqInfo?.Title})\n\n" +
                        $"No reply was received within {windowSec} seconds.\n\n" +
                        "The approval has been completed successfully.\n\n" +
                        "This request is now closed.");
                    _log.LogInformation("WA-TIMEOUT: auto-approved SessionId={Sid} RequestId={Id} ApproverId={Aid}",
                        p.Id, p.RequestId, p.ApproverId);
                }
                else
                {
                    // The step was likely already actioned via another channel — nothing to send.
                    _log.LogWarning("WA-TIMEOUT: execution not ok SessionId={Sid} RequestId={Id} err={Err}",
                        p.Id, p.RequestId, err);
                }
            }
            catch (Exception ex)
            {
                // One session must never stop the rest of the batch from being approved.
                _log.LogError(ex, "WA-TIMEOUT: auto-approve failed SessionId={Sid} RequestId={Id}", p.Id, p.RequestId);
            }
        }
    }
}
